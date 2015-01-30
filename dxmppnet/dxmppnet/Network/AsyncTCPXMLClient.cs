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

using System.Collections.Generic;
using System.Collections.Concurrent;


namespace DXMPP
{
	namespace Network
	{
		internal class AsyncTCPXMLClient : IDisposable
		{

			int DebugLevel = 0;

			string Hostname;
			int Portnumber;
			TcpClient Client;
			SslStream TLSStream;
			Stream ActiveStream;
			Timer KeepAliveTimer;
			MojsStream XMLStream;

			const int SendTimeout = 20000;

			private void Log(int Level, string Message, params object[] FormatObjs)
			{
				if (Level <= DebugLevel)
					Console.WriteLine(Message, FormatObjs);
			}

			private static bool ServerValidationCallback(object Sender,
				X509Certificate Certificate, X509Chain Chain, SslPolicyErrors PolicyErrors)
			{
				switch (PolicyErrors)
				{
					case SslPolicyErrors.RemoteCertificateNameMismatch:
						//throw new Exception( "Client's name mismatch. End communication ...\n" );
						return false;

					case SslPolicyErrors.RemoteCertificateNotAvailable:
						//Console.WriteLine( "Client's certificate not available. End communication ...\n" );
						return false;

					case SslPolicyErrors.RemoteCertificateChainErrors:
						//Console.WriteLine( "Client's certificate validation failed. End communication ...\n" );
						return false;

				}

				return true;
			}

			public void ConnectTLS(bool AllowSelfSignedCertificates)
			{

				TextRead();
				ClearRawTextData();
				lock (Client)
				{
					RemoteCertificateValidationCallback CertValidationCallback =
						new RemoteCertificateValidationCallback(ServerValidationCallback);


					try
					{
						TLSStream = new SslStream(ActiveStream, true, CertValidationCallback);
						TLSStream.WriteTimeout = SendTimeout;
						TLSStream.AuthenticateAsClient(Hostname);
						ActiveStream = TLSStream;
					}
					catch (System.Exception ex)
					{						
						Log(0, "TLS Error(s): {0}", ex.ToString());
						throw new Exception("Connection failed due to TLS errors: " + ex.ToString());
					}
				}

			}

			ConcurrentQueue<XElement> Documents = new ConcurrentQueue<XElement>();
			public XElement FetchXMLDocument()
			{
				XElement RVal;
				if (Documents.TryDequeue(out RVal))
				{
					Log(2, "Dequeued document: {0}", RVal.ToString());
					return RVal;
				}

				return null;
			}

			public delegate void OnDataCallback();
			OnDataCallback OnData;

			class Events
			{
				public enum EventType
				{
					GotData
				}

				public Events(Events.EventType What)
				{
					this.What = What;
				}

				public EventType What;
			}

			System.Collections.Concurrent.ConcurrentQueue<Events> NewEvents = new System.Collections.Concurrent.ConcurrentQueue<Events>();

			public delegate void OnDisconnectCallback();
			OnDisconnectCallback OnDisconnect;

			string IncomingData = string.Empty;

			public enum ReadMode
			{
				Text,
				XML
			}

			ReadMode Mode;

			public void SetReadMode(ReadMode Mode)
			{
				lock (Client)
				{
					if (Mode == this.Mode)
						return;

					Log(1, "Switching to read mode: " + Mode.ToString ());

					if (Mode == ReadMode.XML)
					{
						RestartXMLReader = true;
						XMLStream.ClearStart();
					}

					this.Mode = Mode;

					if (this.Mode == ReadMode.Text)
						XMLStream.Stop();

				}
			}

			void PushDataToXMLParserStream()
			{
				lock (Client)
				{
					try
					{
						if (Client.Available == 0)
							return;

						int NrToGet = Client.Available;

						byte[] IncomingBuffer = new byte[NrToGet];

						int NrGot = ActiveStream.Read(IncomingBuffer, 0, NrToGet);
						byte[] DataToSend = null;
						if (NrGot == NrToGet)
							DataToSend = IncomingBuffer;
						else
						{
							DataToSend = new byte[NrGot];
							Buffer.BlockCopy(IncomingBuffer, 0, DataToSend, 0, NrGot);
						}

						lock (IncomingData)
						{
							if (DebugLevel >= 2)
							{
								string PushData = Encoding.UTF8.GetString(DataToSend);
								Log(2, "Push Data to XmlStream: {0}", PushData);
							}
							XMLStream.PushStringData(DataToSend);
						}
					}
					catch(Exception e)
					{
						Log(1, "OnDisconnect in PushData: {0}", e.ToString());
						if (OnDisconnect == null)
							OnDisconnect.Invoke();
					}
				}
			}

			void TextRead()
			{
				lock (Client)
				{
					try
					{
						if (Client.Available == 0)
							return;

						int NrToGet = Client.Available;

						byte[] IncomingBuffer = new byte[NrToGet];

						int NrGot = ActiveStream.Read(IncomingBuffer, 0, NrToGet);
						lock (IncomingData)
						{
							string NewData = Encoding.UTF8.GetString(IncomingBuffer, 0, NrGot);
							/*Console.WriteLine ("+++");
							Console.WriteLine (NewData);
							Console.WriteLine ("---");*/
							IncomingData += NewData;
							Log(2, "Enqueing incommingdata: {0}", NewData);
						}

						NewEvents.Enqueue(new Events(Events.EventType.GotData));
					}
					catch(Exception e)
					{
						Log(1, "OnDisconnect in TextRead: {0}", e.ToString());
						if (OnDisconnect == null)
							OnDisconnect.Invoke();
					}
				}
			}
			bool RestartXMLReader = false;

			void InnerXMLRead()
			{
				if (!XMLStream.HasData)
					return;

				XmlReaderSettings Settings = new XmlReaderSettings();
				Settings.ValidationType = ValidationType.None;
				Settings.ConformanceLevel = ConformanceLevel.Fragment;
				XmlReader Reader = XmlReader.Create(XMLStream, Settings);

				XElement RootNode = null;
				XElement CurrentElement = null;

				Log(2, "InnerXmlRead");

				while (Reader.Read() && !RestartXMLReader)
				{
					Log(2, "InnerXmlRead: NodeType: {0}, LocalName: {1}", Reader.NodeType, Reader.LocalName);
					switch (Reader.NodeType)
					{
						case XmlNodeType.Attribute:
							break;
						case XmlNodeType.CDATA:
							{
								if (CurrentElement == null)
									continue;

								XCData PureData = new XCData(Reader.Value);
								CurrentElement.Add(PureData);
							}
							break;
						case XmlNodeType.Comment:
							break;
						case XmlNodeType.Document:
							break;
						case XmlNodeType.DocumentFragment:
							break;
						case XmlNodeType.DocumentType:
							break;
						case XmlNodeType.Element:
							{
								bool SelfClosing = Reader.IsEmptyElement;

								XElement NewElement = new XElement(Reader.LocalName);
								LoadAttributesFromReaderToElement(Reader, NewElement);

								if (RootNode == null)
								{
									RootNode = NewElement;
                                    CurrentElement = RootNode;

									if (SelfClosing)
									{
										Log(2, "Enqueing document: {0}", RootNode.ToString());
										Documents.Enqueue(RootNode);
										RootNode = CurrentElement = null;
										NewEvents.Enqueue(new Events(Events.EventType.GotData));
									}
								}
								else
								{
									CurrentElement.Add(NewElement);
									if (!SelfClosing)
										CurrentElement = NewElement;
								}								

								break;
							}
						case XmlNodeType.EndElement:
							{
								if (CurrentElement == null)
									continue;

								if (CurrentElement == RootNode)
								{
									Log(2, "Enqueing document: {0}", RootNode.ToString());
									Documents.Enqueue(RootNode);
									RootNode = CurrentElement = null;

									NewEvents.Enqueue(new Events(Events.EventType.GotData));
								}
								else
									CurrentElement = CurrentElement.Parent;
							}
							break;
						case XmlNodeType.EndEntity:
							break;
						case XmlNodeType.Entity:
							break;
						case XmlNodeType.EntityReference:
							break;
						case XmlNodeType.None:
							break;
						case XmlNodeType.ProcessingInstruction:
							break;
						case XmlNodeType.SignificantWhitespace:
							break;
						case XmlNodeType.Text:
							{
								if (CurrentElement == null)
									continue;

								XText PureText = new XText(Reader.Value);
								CurrentElement.Add(PureText);
							}
							break;
						case XmlNodeType.Whitespace:
							break;
						case XmlNodeType.XmlDeclaration:
							break;

					}
				}
			}

			void BlockingXMLRead()
			{
				while (!KillXMLParser)
				{
					System.Threading.Thread.Sleep(20);

					if (Mode != ReadMode.XML)
						continue;

					try
					{
						RestartXMLReader = false;
						InnerXMLRead();
					}
					catch (Exception e)
					{
						Log(1, "Exception in blockingxmlread: {0}", e.ToString());
						int breakhere = 1;
						// No
					}

				}
			}

			static void LoadAttributesFromReaderToElement(XmlReader Reader, XElement Element)
			{
				if (Reader.HasValue)
					Element.SetValue(Reader.Value);

				if (Reader.HasAttributes)
				{
					while (Reader.MoveToNextAttribute())
					{
						string attrname = Reader.LocalName;
						if (Reader.ReadAttributeValue() && attrname != "xmlns")
							Element.SetAttributeValue(attrname, Reader.Value);
					}
				}
			}



			void RunIO()
			{
				const int NrFramesToSleep = 500;
				int NrToSleep = NrFramesToSleep;
				while (!KillIO)
				{
					if (NrToSleep-- < 1)
					{
						Thread.Sleep(10);
						NrToSleep = NrFramesToSleep;
					}
					switch (Mode)
					{
						case ReadMode.Text:
							TextRead();
							break;
						case ReadMode.XML:
							PushDataToXMLParserStream();
							break;
					}
				}
			}

			void RunEvents()
			{
				while (!KillEvents)
				{
					System.Threading.Thread.Sleep(20);

					try
					{
						Events Event;
						while (NewEvents.TryDequeue(out Event))
						{
							switch (Event.What)
							{
								case Events.EventType.GotData:
									{
										if (OnData != null)
											OnData.Invoke();
									}
									break;
							}
						}
					}
					catch(System.Exception ex)
					{
						if (System.Diagnostics.Debugger.IsAttached)
							System.Diagnostics.Debugger.Break();
					}
				}
			}
			volatile bool KillIO = false;
			volatile bool KillXMLParser = false;
			volatile bool KillEvents = false;

			Thread IOThread;
			Thread ParseXMLThread;
			Thread EventsThread;

			public string GetRawTextData()
			{
				lock (IncomingData)
				{
					Log(2, "Dequeing incommingdata: {0}", IncomingData);
					return IncomingData;
				}
			}

			public void ClearRawTextData()
			{
				lock (IncomingData)
				{
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
					TimeoutSeconds * 1000,
					TimeoutSeconds * 1000);

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
					byte[] OutgoingBuffer = Encoding.UTF8.GetBytes(Data);
					lock (ClientWriteLock)
					{
						LastSentDataToSocket = DateTime.Now;
						ActiveStream.Write(OutgoingBuffer, 0, OutgoingBuffer.Length);
						ActiveStream.Flush();
					}
				}
				catch (System.Exception ex)
				{
					Console.WriteLine("Write text to socket failed: " + ex.ToString());
					if (OnDisconnect != null)
						OnDisconnect.Invoke();
				}
			}

			public AsyncTCPXMLClient(string Hostname, int Portnumber, OnDataCallback OnData, OnDisconnectCallback OnDisconnect)
			{
				this.Hostname = Hostname;
				this.Portnumber = Portnumber;
				this.OnData = OnData;
				this.OnDisconnect = OnDisconnect;

				XMLStream = new MojsStream();
				Client = new TcpClient(this.Hostname, this.Portnumber);
				ActiveStream = Client.GetStream();

				Client.SendTimeout = SendTimeout;

				IOThread = new Thread(new ThreadStart(RunIO));
				IOThread.Name = "DXMPPNet IO";
				IOThread.Start();

				EventsThread = new Thread(new ThreadStart(RunEvents));
				EventsThread.Name = "DXMPPNet Events";
				EventsThread.Start();

				ParseXMLThread = new Thread(new ThreadStart(BlockingXMLRead));
				ParseXMLThread.Name = "DXMPPNet Parse XML";
				ParseXMLThread.Start();
			}

			#region IDisposable implementation

			public void Dispose()
			{
				KillIO = true;
				KillXMLParser = true;
				KillEvents = true;
				XMLStream.Stop();

				if (KeepAliveTimer != null)
				{
					KeepAliveTimer.Dispose();
					KeepAliveTimer = null;
				}

				if (IOThread != null)
					IOThread.Join();
				if (ParseXMLThread != null)
					ParseXMLThread.Join();
				if (EventsThread != null)
					EventsThread.Join();

				if (Client != null)
				{
					Client.Close();
					Client = null;
				}
			}

			#endregion
		}
	}
}

