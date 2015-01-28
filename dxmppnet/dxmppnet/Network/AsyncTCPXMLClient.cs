using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using System.Threading;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using System.Collections.Generic;
using System.Collections.Concurrent;


namespace DXMPP
{
	namespace Network
	{
		internal class AsyncTCPXMLClient : IDisposable
		{
			string Hostname;
			int Portnumber;
			TcpClient Client;
			SslStream TLSStream;
			Stream ActiveStream;
            Timer KeepAliveTimer;

			const int SendTimeout = 100;

			private static bool ServerValidationCallback(object  Sender, 
				X509Certificate Certificate,  X509Chain  Chain,  SslPolicyErrors  PolicyErrors)
			{
				switch  (PolicyErrors)
				{
				case SslPolicyErrors.RemoteCertificateNameMismatch:
					//throw new Exception( "Client's name mismatch. End communication ...\n" );
					return false;

				case SslPolicyErrors.RemoteCertificateNotAvailable:
					//Console.WriteLine( "Client's certificate not available. End communication ...\n" );
					return false ;

				case SslPolicyErrors.RemoteCertificateChainErrors:
					//Console.WriteLine( "Client's certificate validation failed. End communication ...\n" );
					return false ;

				}
					
				return true;
			}

			public void ConnectTLS(bool AllowSelfSignedCertificates)
			{

				TextRead ();
				ClearRawTextData ();
				lock (Client) {
					RemoteCertificateValidationCallback CertValidationCallback =
						new RemoteCertificateValidationCallback (ServerValidationCallback);


					try
					{
						TLSStream = new SslStream (ActiveStream, true, CertValidationCallback);
						TLSStream.WriteTimeout = SendTimeout;
						TLSStream.AuthenticateAsClient (Hostname);
						ActiveStream = TLSStream;
					}
					catch (System.Exception ex){
                        Console.WriteLine("TLS Error(s):");
                        Console.WriteLine(ex.ToString());
                        throw new Exception("Connection failed due to TLS errors: " + ex.ToString());
					}
				}

			}

			ConcurrentQueue<XElement> Documents = new ConcurrentQueue<XElement>();
			public XElement FetchXMLDocument()
			{
				XElement RVal;
				if (Documents.TryDequeue (out RVal)) {
					return RVal;
				}

				return null;
			}

			public delegate void OnDataCallback();
			OnDataCallback OnData;

            public delegate void OnDisconnectCallback();
            OnDisconnectCallback OnDisconnect;

            volatile bool KillIO = false;
			string IncomingData = string.Empty;

			public enum ReadMode
			{
				Text,
				XML
			}

			ReadMode Mode;
			XmlReader Reader;
			Task<string> OngoingXmlTask;
			Task<bool> OngoingReadTask;

			public void SetReadMode(ReadMode Mode)
			{
				try
				{
					lock (Client) 
					{
						if (Mode == this.Mode)
							return;

						//Console.WriteLine ("Switching to read mode: " + Mode.ToString ());
						this.Mode = Mode;

						if (this.Mode == ReadMode.XML) {
						}

						if (this.Mode == ReadMode.Text) 
						{
							try {
								if (OngoingXmlTask != null) {
									//OngoingXmlTask.Wait();
									OngoingXmlTask = null;
								}
								if (OngoingReadTask != null) {
									//OngoingReadTask.Wait();
									OngoingReadTask = null;
								}
								if (Reader != null) {
									try
									{
										Reader.Dispose ();
									}
									catch
									{
									}
									Reader = null;
								}
							} 
							catch {
							}
						}
					}
				}
				catch(System.Exception ex) {
					Console.WriteLine ("Set read mode threw " + ex.ToString ());
				}
			}



			void TextRead()
			{
				lock(Client)
				{
					if (Client.Available == 0)
						return;

					int NrToGet = Client.Available;

					byte[] IncomingBuffer = new byte[NrToGet];

					int NrGot = ActiveStream.Read (IncomingBuffer, 0, NrToGet);
					lock (IncomingData) {
						string NewData = Encoding.UTF8.GetString (IncomingBuffer, 0, NrGot);
						/*Console.WriteLine ("+++");
						Console.WriteLine (NewData);
						Console.WriteLine ("---");*/
						IncomingData += NewData;
					}

					OnData.Invoke ();
				}
			}

			void LoadAttributesFromReaderToElement(XElement Element)
			{
				if(Reader.HasValue)
					Element.SetValue (Reader.Value);

				if (Reader.HasAttributes) {
					while (Reader.MoveToNextAttribute ()) {
						string attrname = Reader.LocalName;
						if (Reader.ReadAttributeValue () && attrname != "xmlns")
							Element.SetAttributeValue (attrname, Reader.Value);
					}
				}
			}

			XElement root;
			XElement parent;
            //int ParentDepth;

			void XMLRead()
			{
				//lock(Client)
				{
					if (OngoingReadTask == null) {
						XmlReaderSettings settings = new XmlReaderSettings ();
						settings.ConformanceLevel = ConformanceLevel.Fragment;
						settings.CloseInput = false;
						settings.IgnoreWhitespace = true;
						settings.DtdProcessing = DtdProcessing.Ignore;
						settings.ValidationType = ValidationType.None;
						settings.Async = true;
						settings.IgnoreProcessingInstructions = true;
						settings.ValidationFlags = System.Xml.Schema.XmlSchemaValidationFlags.None;
						Reader = XmlReader.Create (ActiveStream, settings);
						OngoingReadTask = Reader.ReadAsync ();
						root = parent = null;
						return;
					}

					if (OngoingReadTask.IsCompleted) {

						bool IsNewRootNode = false;
						bool NeedRecurse = true;
						if( root == null )
						{
							IsNewRootNode = true;

                            if (Reader.NodeType == XmlNodeType.EndElement)
                            {
                                if (Reader.LocalName == "</stream:stream>")
                                {
                                    if (OnDisconnect != null)
                                        OnDisconnect.Invoke();
                                }
                            }

							if (Reader.NodeType != XmlNodeType.Element) {
								OngoingReadTask = Reader.ReadAsync ();
								return;
							}

							NeedRecurse = !Reader.IsEmptyElement;

							root = new XElement (Reader.LocalName);
                            //ParentDepth = Reader.Depth;
							LoadAttributesFromReaderToElement (root);
							parent = root;
						}

                        /*if ((parent != root) &&
                            Reader.Depth <= ParentDepth)
                        {
                            parent = parent.Parent;
                            ParentDepth = Reader.Depth;
                        }*/


						if (!IsNewRootNode) {
							switch (Reader.NodeType) {
							case XmlNodeType.EndElement:
								{
                                    if (parent != root)
                                        parent = parent.Parent;
                                    else
                                        NeedRecurse = false;
                                    
                                    break;
								}
							case XmlNodeType.CDATA:
								{
									parent.Add (new XCData (Reader.Value));
									break;
								}
							case XmlNodeType.Text:
								{
									parent.Add (new XText (Reader.Value));
									break;
								}
							case XmlNodeType.Element:
								{
                                    //ParentDepth = Reader.Depth;                                    
									XElement Child = new XElement (Reader.LocalName);
									LoadAttributesFromReaderToElement (Child);
									parent.Add (Child);
                                    if( !Reader.IsEmptyElement )
									    parent = Child;
									break;
								}
							}
						}


						if (NeedRecurse) {
							OngoingReadTask = Reader.ReadAsync ();
							return;
						}

						Documents.Enqueue (root);
						root = parent = null;
						OnData.Invoke ();

						if(Mode == ReadMode.XML)
							OngoingReadTask = Reader.ReadAsync ();
					}
				}
			}

			void RunIO()
			{
				const int NrFramesToSleep = 500;
				int NrToSleep = NrFramesToSleep;
                while (!KillIO) {
					if (NrToSleep-- < 1)
					{
						Thread.Sleep (10);
						NrToSleep = NrFramesToSleep;
                    }
					switch(Mode)
					{
					case ReadMode.Text:
						TextRead ();
						break;
					case ReadMode.XML:
						XMLRead ();
						break;
					}
				}
			}
			Thread IOThread;

			public string GetRawTextData()
			{
				lock(IncomingData){
					return IncomingData;
				}
			}

			public void ClearRawTextData()
			{
				lock(IncomingData){
					IncomingData = string.Empty;
				}
			}

			void SetRawTextDataUnlocked(string Data)
			{
				IncomingData = Data;
			}

            int KeepAliveTimerIntervalSeconds;
            string WhiteSpaceToSend;
            void SendKeepAliveWhitespace(object State)
            {
                if (LastSentDataToSocket > DateTime.Now.AddSeconds(-KeepAliveTimerIntervalSeconds))
                    return;

                WriteTextToSocket(WhiteSpaceToSend);
            }


            public void SetKeepAliveByWhiteSpace(string DataToSend = " ", 
                int TimeoutSeconds = 10)
            {
                WhiteSpaceToSend = DataToSend;
                KeepAliveTimerIntervalSeconds = TimeoutSeconds;
                if (KeepAliveTimer != null)
                    KeepAliveTimer = null;

                KeepAliveTimer = new Timer(new TimerCallback(SendKeepAliveWhitespace), 
                    null, 
                    TimeoutSeconds*1000, 
                    TimeoutSeconds*1000);

                KeepAliveTimerIntervalSeconds = TimeoutSeconds;
            }

            DateTime LastSentDataToSocket;

            object ClientWriteLock = new object();

			public void WriteTextToSocket(string Data)
			{
				/*
				Console.WriteLine (">>>");
				Console.WriteLine (Data);
				Console.WriteLine ("<<<");*/

                try
                {
    				byte[] OutgoingBuffer = Encoding.UTF8.GetBytes (Data);
                    lock (ClientWriteLock) {
                        LastSentDataToSocket = DateTime.Now;
    					ActiveStream.Write (OutgoingBuffer, 0, OutgoingBuffer.Length);
    					ActiveStream.Flush ();
    				}
                }
                catch(System.Exception ex)
                {
                    Console.WriteLine("Write text to socket failed: " + ex.ToString());
                    if (OnDisconnect != null)
                        OnDisconnect.Invoke();
                }
			}

			public AsyncTCPXMLClient ( string Hostname, int Portnumber, OnDataCallback OnData, OnDisconnectCallback OnDisconnect )
			{
				this.Hostname = Hostname;
				this.Portnumber = Portnumber;
				this.OnData = OnData;
                this.OnDisconnect = OnDisconnect;

				Client = new TcpClient (this.Hostname, this.Portnumber);
				ActiveStream = Client.GetStream ();
				//Client.ReceiveBufferSize = 1024;
				//Client.NoDelay = true;
				//Client.NoDelay = true;
				Client.SendTimeout = SendTimeout;

				IOThread = new Thread (new ThreadStart (RunIO));
				IOThread.Start ();
			}

			#region IDisposable implementation

			public void Dispose ()
			{
				KillIO = true;

                if (KeepAliveTimer != null)
                {
                    KeepAliveTimer.Dispose();
                    KeepAliveTimer = null;
                }

				if(IOThread != null)
					IOThread.Join ();

				if (Client != null) {
					Client.Close ();
					Client = null;
				}
			}

			#endregion
		}
	}
}

