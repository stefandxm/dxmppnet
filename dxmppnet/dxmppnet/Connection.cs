﻿using System;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DXMPP
{
    public class Connection : IDisposable
    {
        enum ConnectionState
        {
            NotConnected = 0,
            Connecting,
            WaitingForFeatures,
            Authenticating,
            WaitingForSession,
            Connected,

            ErrorConnecting,
            ErrorAuthenticating,
            ErrorUnknown
        }

        enum AuthenticationState
        {
            None,
            StartTLS,
            SASL,
            Bind,
            Authenticated
        }

        public enum CallbackConnectionState
        {
            NotConnected = 0,
            Connecting,
            Connected,

            ErrorConnecting,
            ErrorAuthenticating,
            ErrorUnknown
        }

        Network.AsyncTCPXMLClient Client;

        bool FeaturesSASL_DigestMD5 = false;
        bool FeaturesSASL_CramMD5 = false;
        bool FeaturesSASL_ScramSHA1 = false;
		bool FeaturesSASL_External = false;
        bool FeaturesSASL_Plain = false;
        bool FeaturesStartTLS;

        ConnectionState CurrentConnectionState;
        AuthenticationState CurrentAuthenticationState;

        string Hostname;
        string Password;
		X509Certificate2 Certificate;
		public bool AllowSelfSignedServerCertificate = false;
		public bool AllowPlainAuthentication = false;
		public bool AllowPlainAuthenticationWithoutTLS = false;

        int Portnumber;
        JID MyJID;

        SASL.SASLMechanism Authentication;

        private readonly object ClientReplacingMutex = new object();
        private long TotalBytesReceivedBeforeLastReconnect = 0;
        private long TotalBytesSentBeforeLastReconnect = 0;

        void Reset()
        {
            try
            {
                Client.Dispose();
                Roster.Dispose();
            }
            catch
            {
            }
        }

        void InitTLS()
        {
            try
            {
                Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.Text);
                Client.ClearRawTextData();
                Client.ConnectTLS();
                OpenXMPPStream();
            }
            catch
            {
                Console.WriteLine("Failed to connect with TLS");
                CurrentAuthenticationState = AuthenticationState.None;
                CurrentConnectionState = ConnectionState.ErrorUnknown;
                BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
            }
        }

        public void Connect()
        {
            lock (ClientReplacingMutex)
            {
                if (Client != null)
                {
                    Client.Dispose();
                    TotalBytesReceivedBeforeLastReconnect = GetTotalBytesReceived();
                    TotalBytesSentBeforeLastReconnect = GetTotalBytesSent();
                    Client = null;
                }
                BroadcastConnectionState(CallbackConnectionState.Connecting);
                try
                {
					Client = new Network.AsyncTCPXMLClient(Hostname, 
					                                       Portnumber, 
					                                       Certificate, 
					                                       AllowSelfSignedServerCertificate,  
					                                       ClientGotData, 
					                                       ClientDisconnected);
                }
                catch
                {
                    ClientDisconnected();
                    return;
                }
            }
            OpenXMPPStream();

            if (MyJID.GetResource() == string.Empty)
                MyJID.SetResource(System.Guid.NewGuid().ToString());
            this.RosterMaintainer = new Roster(this);
        }

        public long GetTotalBytesSent()
        {
            Network.AsyncTCPXMLClient c;
            lock (ClientReplacingMutex)
                c = Client;
            if (c != null)
                return TotalBytesSentBeforeLastReconnect + c.TotalDataSent.Read();
            else
                return TotalBytesSentBeforeLastReconnect;

        }
        public long GetTotalBytesReceived()
        {
            Network.AsyncTCPXMLClient c;
            lock (ClientReplacingMutex)
                c = Client;
            if (c != null)
                return TotalBytesReceivedBeforeLastReconnect +
                    c.TotalDataReceivedForTlsConnection.Read() +
                    c.TotalDataReceivedInTextMode.Read() +
                    c.TotalDataReceivedInXmlMode.Read();
            else
                return TotalBytesReceivedBeforeLastReconnect;
        }

        private static XElement RemoveAllNamespaces(XElement xmlDocument)
        {
            if (!xmlDocument.HasElements)
            {
                XElement xElement = new XElement(xmlDocument.Name.LocalName);
                xElement.Value = xmlDocument.Value;
                foreach (XAttribute attribute in xmlDocument.Attributes())
                    xElement.Add(attribute);
                return xElement;
            }

            return new XElement(xmlDocument.Name.LocalName, xmlDocument.Elements().Select(el => RemoveAllNamespaces(el)));
        }

        void OpenXMPPStream()
        {
            Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.Text);
            CurrentConnectionState = ConnectionState.WaitingForFeatures;

            string Stream = string.Empty;
            Stream += "<?xml version='1.0' encoding='utf-8'?>";
            Stream += "<stream:stream";
            Stream += " from = '" + MyJID.GetBareJID() + "'";
            Stream += " to = '" + MyJID.GetDomain() + "'";
            Stream += " version='1.0'";
            Stream += " xml:lang='en'";
            Stream += " xmlns='jabber:client'";
            Stream += " xmlns:stream='http://etherx.jabber.org/streams'>";

            Client.WriteTextToSocket(Stream);
        }
        string LocalBeginStream = string.Empty;
        string LocalEndStream = "</stream:stream>";

        // this is ok
        void CheckStreamForFeatures()
        {
            FeaturesSASL_DigestMD5 = false;
            FeaturesSASL_CramMD5 = false;
            FeaturesSASL_ScramSHA1 = false;
			FeaturesSASL_External = false;
            FeaturesSASL_Plain = false;
            FeaturesStartTLS = false;


            string str = Client.GetRawTextData();
            string EndOfFeatures = "</stream:features>";
            int IndexOfEndOfFeatures = str.IndexOf(EndOfFeatures);
            if (IndexOfEndOfFeatures == -1)
                return;

            if (CurrentAuthenticationState == AuthenticationState.SASL)
            {
                Client.ClearRawTextData();
                CurrentConnectionState = ConnectionState.Authenticating;
                return;
            }
            if (CurrentAuthenticationState == AuthenticationState.Bind)
            {
                Client.ClearRawTextData();
                CurrentConnectionState = ConnectionState.Authenticating;
                Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);
                BindResource();
                return;
            }


            int IndexofOpenFeatures = str.IndexOf("<stream:fea");
            if (IndexofOpenFeatures == -1)
                throw new Exception("I messed up :(");

            LocalBeginStream = str.Substring(0, IndexofOpenFeatures);

            string xml = LocalBeginStream +
                str.Substring(IndexofOpenFeatures, IndexOfEndOfFeatures - IndexofOpenFeatures + EndOfFeatures.Length) +
                LocalEndStream;

            XElement CurrDoc = RemoveAllNamespaces(XElement.Parse(xml));

            Client.ClearRawTextData();

            XElement StartTLSElement = CurrDoc.XPathSelectElement("//starttls");
            if (StartTLSElement != null)
            {
                FeaturesStartTLS = true;
            }

            var Mechanisms = CurrDoc.XPathSelectElements("//mechanism");
            foreach (XElement Mechanism in Mechanisms)
            {
                string MechanismName = Mechanism.Value;
                switch (MechanismName.ToUpper())
                {
                    case "DIGEST-MD5":
                        FeaturesSASL_DigestMD5 = true;
                        break;
                    case "CRAM-MD5":
                        FeaturesSASL_CramMD5 = true;
                        break;
                    case "SCRAM-SHA-1":
                        FeaturesSASL_ScramSHA1 = true;
                        break;
					case "EXTERNAL":
						FeaturesSASL_External = true;
						break;
                    case "PLAIN":
                        FeaturesSASL_Plain = true;
                        break;
                }
            }

            CurrentConnectionState = ConnectionState.Authenticating;

            if (CurrentAuthenticationState != AuthenticationState.StartTLS)
            {
                if (FeaturesStartTLS)
                {
                    CurrentAuthenticationState = AuthenticationState.StartTLS;

                    Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);
                    string StartTLS = "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>";
                    Client.WriteTextToSocket(StartTLS);
                    return;
                }
            }

            CurrentAuthenticationState = AuthenticationState.SASL;
            Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);

			if(FeaturesSASL_External && Certificate != null)
			{
				Console.WriteLine ("Authenticating with EXTERNAL");
				Authentication = new DXMPP.SASL.SASL_Mechanism_EXTERNAL(Client, MyJID, Certificate);
				Authentication.Begin();
				return;
			}

            if(FeaturesSASL_ScramSHA1)
			{
				Console.WriteLine ("Authenticating with SCRAM-SHA-1");
                Authentication = new DXMPP.SASL.SASL_Mechanism_SCRAM_SHA1(Client, MyJID, Password);
				Authentication.Begin();
				return;
			}

			/*if(FeaturesSASL_DigestMD5)
			{
				Console.WriteLine ("Deadlock because i havent implemented digest md5!");
				//Authentication = new  SASL::Weak::SASL_Mechanism_DigestMD5 ( Client , MyJID, Password),
				//Authentication->Begin();
				return;
			}*/
			if ( (FeaturesSASL_Plain && AllowPlainAuthentication) && 
			    Client.IsConnectedViaTLS || AllowPlainAuthenticationWithoutTLS )
            {
				Console.WriteLine ("WARNING: Authenticating with PLAIN");
                Authentication = new DXMPP.SASL.Weak.SASLMechanism_PLAIN(Client, MyJID, Password);
                Authentication.Begin();
                return;
            }

        }


        // who vomited?
        void CheckForTLSProceed(XElement Doc)
        {
            if (Doc.Name.LocalName != "proceed")
            {
                Console.WriteLine("No proceed tag; B0rked SSL?!");

                BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
                CurrentConnectionState = ConnectionState.ErrorUnknown;
                return;
            }

            if (CurrentAuthenticationState == AuthenticationState.StartTLS)
                InitTLS();
        }
        void CheckForWaitingForSession(XElement Doc)
        {
            if (Doc.Name.LocalName != "iq")
            {
                Console.WriteLine("No iqnode?!");
                BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
                CurrentConnectionState = ConnectionState.ErrorUnknown;
                return;
            }

            // TODO: Verify iq response..

            string Presence = "<presence/>";
            Client.WriteTextToSocket(Presence);
            CurrentConnectionState = ConnectionState.Connected;
            Client.SetKeepAliveByWhiteSpace(" ", 10);
        }
        void CheckForBindSuccess(XElement Doc)
        {
            if (Doc.Name.LocalName != "iq")
            {
                Console.WriteLine("no iqnode?!");
                BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
                CurrentConnectionState = ConnectionState.ErrorUnknown;
                return;
            }

            string StartSession = "<iq type='set' id='1'><session xmlns='urn:ietf:params:xml:ns:xmpp-session'/></iq>";
            Client.WriteTextToSocket(StartSession);
            CurrentConnectionState = ConnectionState.WaitingForSession;
            CurrentAuthenticationState = AuthenticationState.Authenticated;
        }
        void CheckForSASLData(XElement Doc)
        {
            XElement Challenge = Doc.Name.LocalName == "challenge" ? Doc : null;
            XElement Success = Doc.Name.LocalName == "success" ? Doc : null;

            if (Challenge == null && Success == null)
            {
                Console.WriteLine("Bad authentication");
                BroadcastConnectionState(CallbackConnectionState.ErrorAuthenticating);
                CurrentConnectionState = ConnectionState.ErrorAuthenticating;

                return;
            }

            if (Challenge != null)
            {
                Authentication.Challenge(Challenge);
            }

            if (Success != null)
            {
                if (!Authentication.Verify(Success))
                {
                    Console.WriteLine("Bad success verification from server");
                    BroadcastConnectionState(CallbackConnectionState.ErrorAuthenticating);
                    CurrentConnectionState = ConnectionState.ErrorConnecting;

                    return;
                }

                StartBind();
            }
        }
        void CheckStreamForAuthenticationData(XElement Doc)
        {
            switch (CurrentAuthenticationState)
            {
                case AuthenticationState.StartTLS:
                    CheckForTLSProceed(Doc);
                    break;
                case AuthenticationState.SASL:
                    CheckForSASLData(Doc);
                    break;
                case AuthenticationState.Bind:
                    CheckForBindSuccess(Doc);
                    break;
                case AuthenticationState.Authenticated:
                    break;
                default:
                    break;
            }

        }
        void CheckStreamForStanza(XElement Doc)
        {
            
            XElement message = Doc.Name.LocalName == "message" ? Doc : null;
			XElement iq = Doc.Name.LocalName == "iq" ? Doc : null;

			if (message != null && OnStanzaMessage != null)
			{
				StanzaMessage NewStanza = new StanzaMessage(message);
				OnStanzaMessage.Invoke(NewStanza);
			}

			if (iq != null && OnStanzaIQ != null)
			{
				StanzaIQ NewStanza = new StanzaIQ(iq);
				OnStanzaIQ.Invoke(NewStanza);
			}
        }
        void CheckForPresence(XElement Doc)
        {
            XElement presence = Doc.Name.LocalName == "presence" ? Doc : null;

            if (presence == null)
                return;

            if (OnPresence != null)
                OnPresence.Invoke(presence);
        }

        // this is ok (invalid xml)
        void CheckForStreamEnd()
        {
            string Rawdata = Client.GetRawTextData();
            bool Disconnected = false;

            if (Rawdata.IndexOf("</stream::stream>") != -1)
                Disconnected = true;
            if (Rawdata.IndexOf("</stream>") != -1)
                Disconnected = true;

            if (!Disconnected)
                return;

            Client.ClearRawTextData();
            CurrentConnectionState = ConnectionState.ErrorUnknown;
            BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
        }


        // this is ok
        void CheckStreamForValidXML()
        {
            if (CurrentConnectionState == ConnectionState.WaitingForFeatures)
            {
                BroadcastConnectionState(CallbackConnectionState.Connecting);
                CheckStreamForFeatures();
                return;
            }
            XElement Doc = null;

            Client.SetReadMode(DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);

            do
            {
                Doc = Client.FetchXMLDocument();
                /*if(Doc != null)
				{
					Console.WriteLine("got xml:");
					Console.WriteLine(Doc.ToString());
				}*/

                switch (CurrentConnectionState)
                {
                    case ConnectionState.WaitingForSession:
                        BroadcastConnectionState(CallbackConnectionState.Connecting);
                        break;
                    case ConnectionState.WaitingForFeatures:
                        break;
                    case ConnectionState.Authenticating:
                        BroadcastConnectionState(CallbackConnectionState.Connecting);
                        break;
                    case ConnectionState.Connected:
                        BroadcastConnectionState(CallbackConnectionState.Connected);
                        break;
                    default:
                        break;
                }


                if (Doc == null)
                    continue;

                //Doc =  RemoveAllNamespaces( XElement.Parse ( LocalBeginStream + Doc.ToString() + LocalEndStream) );

                switch (CurrentConnectionState)
                {
                    case ConnectionState.WaitingForSession:
                        CheckForWaitingForSession(Doc);
                        break;
                    case ConnectionState.WaitingForFeatures:
                        break;
                    case ConnectionState.Authenticating:
                        CheckStreamForAuthenticationData(Doc);
                        break;
                    case ConnectionState.Connected:
                        CheckForPresence(Doc);
                        CheckStreamForStanza(Doc);
                        break;
                    default:
                        break;
                }

            } while (Doc != null);

            CheckForStreamEnd();
        }


        void BindResource()
        {
            // TODO: Make Proper XML ?
            //bind resource..
            string TStream = string.Empty;
            TStream += "<iq type='set' id='bindresource'>";
            TStream += "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>";
            TStream += "<resource>" + MyJID.GetResource() + "</resource>";
            TStream += "</bind>";
            TStream += "</iq>";
            Client.WriteTextToSocket(TStream);
        }

        void StartBind()
        {
            CurrentAuthenticationState = AuthenticationState.Bind;
            OpenXMPPStream();
        }


        void ClientDisconnected()
        {
            CurrentAuthenticationState = AuthenticationState.None;
            CurrentConnectionState = ConnectionState.ErrorUnknown;

            BroadcastConnectionState(CallbackConnectionState.ErrorUnknown);
        }

        void ClientGotData()
        {
            CheckStreamForValidXML();
        }

        CallbackConnectionState PreviouslyBroadcastedState = CallbackConnectionState.NotConnected;

        void BroadcastConnectionState(CallbackConnectionState NewState)
        {
            if (PreviouslyBroadcastedState == NewState)
                return;
            PreviouslyBroadcastedState = NewState;

            if (OnConnectionStateChanged != null)
                OnConnectionStateChanged.Invoke(NewState);
        }


        #region IDisposable implementation

        public void Dispose()
        {
            Client.Dispose();
        }

        #endregion

        internal Network.AsyncTCPXMLClient GetNetworkClient()
        {
            return Client;
        }

        internal delegate void PresenceHandler(XElement Node);
        internal PresenceHandler OnPresence;


        public void Reconnect()
        {
            Reset();
            Connect();
        }

        public void SendStanza(Stanza Data)
        {
            if (this.CurrentConnectionState != ConnectionState.Connected)
                throw new InvalidOperationException("Trying tro send stanza disconnected");

            Data.EnforceAttributes(MyJID);
			//Console.WriteLine("Send stanza {0}", Data);
            Client.WriteTextToSocket(Data.ToString());
        }

        public delegate void OnStanzaMessageCallback(StanzaMessage Data);
        public OnStanzaMessageCallback OnStanzaMessage;

		public delegate void OnStanzaIQCallback(StanzaIQ Data);
		public OnStanzaIQCallback OnStanzaIQ;


        public delegate void OnConnectionStateChangedCallback(CallbackConnectionState NewState);
        public OnConnectionStateChangedCallback OnConnectionStateChanged;

        Roster RosterMaintainer;

        public Roster Roster
        {
            get
            {
                return RosterMaintainer;
            }
        }

		public Connection(string Hostname, 
		                  int Portnumber, 
		                  JID RequestedJID, 
		                  string Password, 
		                  X509Certificate2 Certificate = null)
        {
            this.Hostname = Hostname;
            this.Portnumber = Portnumber;
            this.MyJID = RequestedJID;
            if (MyJID.GetResource() == string.Empty)
                MyJID.SetResource(System.Guid.NewGuid().ToString());
            this.Password = Password;
            this.RosterMaintainer = new Roster(this);
			this.Certificate = Certificate;
        }

		public Connection(string Hostname, 
		                  int Portnumber, 
		                  JID RequestedJID, 
		                  X509Certificate2 Certificate)
			:  this(Hostname, Portnumber, RequestedJID, null, Certificate)
		{			
		}
    }
}

