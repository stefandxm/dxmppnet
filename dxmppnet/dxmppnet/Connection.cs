using System;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Linq;
using System.Threading;

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
		bool FeaturesSASL_Plain = false;
		bool FeaturesStartTLS;

		ConnectionState CurrentConnectionState;
		AuthenticationState CurrentAuthenticationState;

		string Hostname;
		string Password;
		int Portnumber;
		JID MyJID;

		SASL.SASLMechanism Authentication;

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
				Client.ConnectTLS (true);
				OpenXMPPStream();
			}
			catch {
				Console.WriteLine ("Failed to connect with TLS");
				CurrentConnectionState = ConnectionState.ErrorUnknown;
				BroadcastConnectionState (CallbackConnectionState.ErrorUnknown);
			}
		}

		public void Connect()
		{
			if (Client != null) {
				Client.Dispose ();
				Client = null;
			}

            Client = new Network.AsyncTCPXMLClient (Hostname, Portnumber, ClientGotData, ClientDisconnected);
			OpenXMPPStream ();

            if (MyJID.GetResource () == string.Empty)
                MyJID.SetResource (System.Guid.NewGuid ().ToString ());
            this.RosterMaintainer = new Roster(this);

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
			Client.SetReadMode (DXMPP.Network.AsyncTCPXMLClient.ReadMode.Text);
			CurrentConnectionState = ConnectionState.WaitingForFeatures;

			string Stream = string.Empty;
			Stream += "<?xml version='1.0' encoding='utf-8'?>" + System.Environment.NewLine;
			Stream += "<stream:stream" +System.Environment.NewLine;
			Stream += " from = '" + MyJID.GetBareJID() + "'" +System.Environment.NewLine;
			Stream += " to = '" + MyJID.GetDomain() + "'" +System.Environment.NewLine;
			Stream += " version='1.0'" +System.Environment.NewLine;
			Stream += " xml:lang='en'" +System.Environment.NewLine;
			Stream += " xmlns='jabber:client'" +System.Environment.NewLine;
			Stream += " xmlns:stream='http://etherx.jabber.org/streams'>";

			Client.WriteTextToSocket(Stream);
		}
		string LocalBeginStream = string.Empty;
		string LocalEndStream = "</stream:stream>";

		// this is ok
		void CheckStreamForFeatures()
		{
			string str = Client.GetRawTextData ();
			string EndOfFeatures = "</stream:features>";
			int IndexOfEndOfFeatures = str.IndexOf (EndOfFeatures);
			if(IndexOfEndOfFeatures == -1)
				return;

			if (CurrentAuthenticationState == AuthenticationState.SASL) {
				Client.ClearRawTextData ();
	            CurrentConnectionState = ConnectionState.Authenticating;
				return;
			}
			if(CurrentAuthenticationState == AuthenticationState.Bind)
			{
				Client.ClearRawTextData ();
				CurrentConnectionState = ConnectionState.Authenticating;
				Client.SetReadMode (DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);
				BindResource ();
				return;
			}


			int IndexofOpenFeatures = str.IndexOf ("<stream:fea");
			if (IndexofOpenFeatures == -1)
				throw new Exception ("I messed up :(");

			LocalBeginStream = str.Substring (0, IndexofOpenFeatures);

			string xml = LocalBeginStream + 
				str.Substring(IndexofOpenFeatures, IndexOfEndOfFeatures - IndexofOpenFeatures + EndOfFeatures.Length) + 
				LocalEndStream;

			XElement CurrDoc = RemoveAllNamespaces(XElement.Parse(xml));

			Client.ClearRawTextData ();

			XElement StartTLSElement = CurrDoc.XPathSelectElement ("//starttls");
			if (StartTLSElement != null) {
				FeaturesStartTLS = true;
			}

			var Mechanisms = CurrDoc.XPathSelectElements ("//mechanism");
			foreach(XElement Mechanism in Mechanisms)
			{
				string MechanismName = Mechanism.Value;

				switch (MechanismName.ToUpper()) {
				case "DIGEST-MD5":
					FeaturesSASL_DigestMD5 = true;
					break;
				case "CRAM-MD5":
					FeaturesSASL_CramMD5= true;
					break;
				case "SCRAM-SHA-1":
					FeaturesSASL_ScramSHA1 = true;
					break;
				case "PLAIN":
					FeaturesSASL_Plain = true;
					break;
				}
			}

			CurrentConnectionState = ConnectionState.Authenticating;

			if (CurrentAuthenticationState != AuthenticationState.StartTLS) {
				if (FeaturesStartTLS) {
					CurrentAuthenticationState = AuthenticationState.StartTLS;

					Client.SetReadMode (DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);
					string StartTLS = "<starttls xmlns='urn:ietf:params:xml:ns:xmpp-tls'/>";
					Client.WriteTextToSocket (StartTLS);
					return;
				}
			}

			CurrentAuthenticationState = AuthenticationState.SASL;
			Client.SetReadMode (DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);

			/*if(FeaturesSASL_ScramSHA1)
			{
				Console.WriteLine ("Deadlock because i havent implemented scram sha!");
				//Authentication = new  SASL::SASL_Mechanism_SCRAM_SHA1 ( Client, MyJID, Password),
				//Authentication->Begin();
				return;
			}
			if(FeaturesSASL_DigestMD5)
			{
				Console.WriteLine ("Deadlock because i havent implemented digest md5!");
				//Authentication = new  SASL::Weak::SASL_Mechanism_DigestMD5 ( Client , MyJID, Password),
				//Authentication->Begin();
				return;
			}*/
			if(FeaturesSASL_Plain)
			{
				Authentication = new DXMPP.SASL.Weak.SASLMechanism_PLAIN(Client, MyJID, Password);
				Authentication.Begin();
				return;
			}

		}


		// who vomited?
		void CheckForTLSProceed(XElement Doc)
		{
			if(Doc.Name != "proceed")
			{
				Console.WriteLine( "No proceed tag; B0rked SSL?!" );

				BroadcastConnectionState (CallbackConnectionState.ErrorUnknown);
				CurrentConnectionState = ConnectionState.ErrorUnknown;
				return;
			}

			if(CurrentAuthenticationState == AuthenticationState.StartTLS)
				InitTLS();
		}
		void CheckForWaitingForSession(XElement Doc)
		{
			if (Doc.Name.LocalName != "iq") {
				Console.WriteLine ("No iqnode?!");
				BroadcastConnectionState (CallbackConnectionState.ErrorUnknown);
				CurrentConnectionState = ConnectionState.ErrorUnknown;
				return;
			}

			// TODO: Verify iq response..

			string Presence = "<presence/>";
			Client.WriteTextToSocket (Presence);
			CurrentConnectionState = ConnectionState.Connected;
            Client.SetKeepAliveByWhiteSpace(" ", 10);
		}
		void CheckForBindSuccess(XElement Doc)
		{
			if (Doc.Name.LocalName != "iq") {
				Console.WriteLine ("no iqnode?!");
				BroadcastConnectionState (CallbackConnectionState.ErrorUnknown);
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

			if (Challenge == null && Success == null) {
				Console.WriteLine ("Bad authentication");
				BroadcastConnectionState (CallbackConnectionState.ErrorAuthenticating);
				CurrentConnectionState = ConnectionState.ErrorAuthenticating;

				return;
			}

			if (Challenge != null) {
				Authentication.Challenge (Challenge);
			}

			if(Success != null )
			{
				if (!Authentication.Verify (Success)) {
					Console.WriteLine ("Bad success verification from server");
					BroadcastConnectionState (CallbackConnectionState.ErrorAuthenticating);
					CurrentConnectionState = ConnectionState.ErrorConnecting;

					return;
				}

				StartBind ();
			}
		}
		void CheckStreamForAuthenticationData(XElement Doc)
		{
			switch(CurrentAuthenticationState)
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
			if (OnStanza == null)
				return;

			XElement message = Doc.Name.LocalName == "message" ? Doc : null;

			if(message == null)
				return;

			Stanza NewStanza = new Stanza (message);
			OnStanza.Invoke (NewStanza);
		}
		void CheckForPresence(XElement Doc)
		{
			XElement presence = Doc.Name.LocalName == "presence" ? Doc : null;

			if(presence == null)
				return;

			if (OnPresence != null)
				OnPresence.Invoke (presence);
		}

		// this is ok (invalid xml)
		void CheckForStreamEnd()		
		{
			string Rawdata = Client.GetRawTextData ();
			bool Disconnected = false;

			if (Rawdata.IndexOf ("</stream::stream>") != -1)
				Disconnected = true;
			if (Rawdata.IndexOf ("</stream>") != -1)
				Disconnected = true;

			if (!Disconnected)
				return;

			Client.ClearRawTextData ();
			CurrentConnectionState = ConnectionState.ErrorUnknown;
			BroadcastConnectionState (CallbackConnectionState.ErrorUnknown);
		}


		// this is ok
		void CheckStreamForValidXML()
		{
			if (CurrentConnectionState == ConnectionState.WaitingForFeatures) {
				BroadcastConnectionState (CallbackConnectionState.Connecting);
				CheckStreamForFeatures ();
				return;
			}
			XElement Doc = null;

			Client.SetReadMode (DXMPP.Network.AsyncTCPXMLClient.ReadMode.XML);

			do {
				Doc = Client.FetchXMLDocument ();
				/*if(Doc != null)
				{
					Console.WriteLine("got xml:");
					Console.WriteLine(Doc.ToString());
				}*/

				if(Doc==null)
					continue;

				//Doc =  RemoveAllNamespaces( XElement.Parse ( LocalBeginStream + Doc.ToString() + LocalEndStream) );

				switch(CurrentConnectionState)
				{
				case ConnectionState.WaitingForSession:
					BroadcastConnectionState(CallbackConnectionState.Connecting);
					CheckForWaitingForSession(Doc);
					break;
				case ConnectionState.WaitingForFeatures:
					break;
				case ConnectionState.Authenticating:
					BroadcastConnectionState(CallbackConnectionState.Connecting);
					CheckStreamForAuthenticationData(Doc);
					break;
				case ConnectionState.Connected:
					BroadcastConnectionState(CallbackConnectionState.Connected);
					CheckForPresence(Doc);
					CheckStreamForStanza(Doc);
					break;
				default:
					break;
				}

			} while(Doc != null);

			CheckForStreamEnd ();
		}


		void BindResource()
		{
			// TODO: Make Proper XML ?
			//bind resource..
			string TStream = string.Empty;
			TStream += "<iq type='set' id='bindresource'>";
			TStream += "<bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'>";
			TStream += "<resource>" + MyJID.GetResource () + "</resource>";
			TStream += "</bind>";
			TStream += "</iq>";
			Client.WriteTextToSocket (TStream);
		}

		void StartBind()
		{
			CurrentAuthenticationState = AuthenticationState.Bind;
			OpenXMPPStream();
		}


		void ClientDisconnected()
		{
			throw new NotImplementedException ();
		}

		void ClientGotData()
		{
			CheckStreamForValidXML ();
		}

		CallbackConnectionState PreviouslyBroadcastedState = CallbackConnectionState.NotConnected;

		void BroadcastConnectionState(CallbackConnectionState NewState)
		{

			if(PreviouslyBroadcastedState == NewState)
				return;
			PreviouslyBroadcastedState = NewState;

            if (OnConnectionStateChanged != null)
                OnConnectionStateChanged.Invoke(NewState);
		}


		#region IDisposable implementation

		public void Dispose ()
		{
			Client.Dispose ();
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

		/*
		 *         RosterMaintaner *Roster;
		*/

		public void SendStanza( Stanza Data )
		{
			Data.EnforceAttributes (MyJID);
			Client.WriteTextToSocket (Data.ToString ());
		}

		public delegate void OnStanzaCallback(Stanza Data);
		public OnStanzaCallback OnStanza;

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

        public Connection (string Hostname, int Portnumber, JID RequestedJID, string Password)
		{
			this.Hostname = Hostname;
			this.Portnumber = Portnumber;
			this.MyJID = RequestedJID;
			if (MyJID.GetResource () == string.Empty)
				MyJID.SetResource (System.Guid.NewGuid ().ToString ());
			this.Password = Password;
            this.RosterMaintainer = new Roster(this);
		}
	}
}

