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
            int DebugLevel = 4;

            private readonly string Hostname;
            private readonly int Portnumber;
            private readonly TcpClient Client;
            private SslStream TLSStream;
            private Stream ActiveStream;
            private Timer KeepAliveTimer;
            private readonly MojsStream XMLStream;

            const int SendTimeout = 20000;

            volatile bool KillIO = false;
            volatile bool KillXMLParser = false;
            volatile bool KillEvents = false;

            private readonly Thread IOThread;
            private readonly Thread ParseXMLThread;
            private readonly Thread EventsThread;


            // bigger than max record length for SSL/TLS
            byte[] ReceiveBuffer = new byte[16384];

            bool IsConnectedViaTLS = false;


            private readonly ConcurrentQueue<XElement> Documents = new ConcurrentQueue<XElement>();
            public delegate void OnDataCallback();
            private readonly OnDataCallback OnData;

            private int KeepAliveTimerIntervalSeconds;
            private string WhiteSpaceToSend;


            private readonly System.Collections.Concurrent.ConcurrentQueue<Events> NewEvents = new System.Collections.Concurrent.ConcurrentQueue<Events>();

            public delegate void OnDisconnectCallback();
            private OnDisconnectCallback OnDisconnect;

            string IncomingData = string.Empty;

            ReadMode Mode;

            DateTime LastSentDataToSocket;

            private readonly object ClientWriteLock = new object();

            public readonly AtomicLongCounter TotalDataSent = new AtomicLongCounter();
            public readonly AtomicLongCounter TotalDataReceivedInXmlMode = new AtomicLongCounter();
            public readonly AtomicLongCounter TotalDataReceivedInTextMode = new AtomicLongCounter();
            public readonly AtomicLongCounter TotalDataReceivedForTlsConnection = new AtomicLongCounter();
            public readonly AtomicLongCounter TotalXmlDocumentsEnqueueud = new AtomicLongCounter();
            public readonly AtomicLongCounter TotalXmlDocumentsDequeued = new AtomicLongCounter();


            private void Log(int Level, string Message, params object[] FormatObjs)
            {
                if (Level <= DebugLevel)
                    Console.WriteLine(Message, FormatObjs);
            }


            private static bool ServerValidationCallback(object Sender,
                X509Certificate Certificate, X509Chain Chain, SslPolicyErrors PolicyErrors)
            {
				return true; // DO NOT COMMIT
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
            {   //TextRead();
                ClearRawTextData();
                lock (Client)
                {
                    byte[] nullbuff;
                    if (Client.Available > 0)
                    {
                        nullbuff = new byte[Client.Available];
                        int BytesRead = Client.GetStream().Read(nullbuff, 0, nullbuff.Length);
                        TotalDataReceivedForTlsConnection.Add(BytesRead);
                    }

                    RemoteCertificateValidationCallback CertValidationCallback =
                        new RemoteCertificateValidationCallback(ServerValidationCallback);


                    try
                    {
                        Console.WriteLine("Connecting with TLS");
                        TLSStream = new SslStream(ActiveStream, true, CertValidationCallback);
                        TLSStream.WriteTimeout = SendTimeout;
                        TLSStream.AuthenticateAsClient(Hostname);
                        ActiveStream = TLSStream;
                        if (Client.Available > 0)
                        {
                            nullbuff = new byte[Client.Available];
                            int BytesRead = Client.GetStream().Read(nullbuff, 0, nullbuff.Length);
                            TotalDataReceivedForTlsConnection.Add(BytesRead);
                        }
                        IsConnectedViaTLS = true;
                        Console.WriteLine("Connected with TLS");
                    }
                    catch (System.Exception ex)
                    {
                        Log(0, "TLS Error(s): {0}", ex.ToString());
                        throw new Exception("Connection failed due to TLS errors: " + ex.ToString());
                    }
                }

            }

            public XElement FetchXMLDocument()
            {
                XElement RVal;
                if (Documents.TryDequeue(out RVal))
                {
                    TotalXmlDocumentsDequeued.Inc1();
                    if (DebugLevel >= 2)
                        Log(2, "Dequeued document: {0}", RVal.ToString());
                    return RVal;
                }

                return null;
            }


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

            public enum ReadMode
            {
                Text,
                XML
            }

            public void SetReadMode(ReadMode Mode)
            {
                lock (Client)
                {
                    if (Mode == this.Mode)
                        return;

                    Log(1, "Switching to read mode: " + Mode.ToString());

                    if (Mode == ReadMode.XML)
                    {
                        XMLStream.ClearStart();
                        Log(3, "XMLStream Cleared and started");
                    }

                    this.Mode = Mode;

                    if (this.Mode == ReadMode.Text)
                        XMLStream.Stop();

                }
            }

            bool PushDataToXMLParserStream()
            {
                lock (Client)
                {
                    try
                    {
                        int Available = Client.Available;
                        if (Available == 0)
                            return false;

                        int NrToGet = IsConnectedViaTLS ? ReceiveBuffer.Length : Available;
                        NrToGet = Math.Min(NrToGet, ReceiveBuffer.Length);
                        int NrGot = 0;

                        do
                        {
                            NrGot = ActiveStream.Read(ReceiveBuffer, 0, NrToGet);
                            if (NrGot < 1)
                                break;
                            TotalDataReceivedInXmlMode.Add(NrGot);


                            byte[] DataToSend = new byte[NrGot];
                            Buffer.BlockCopy(ReceiveBuffer, 0, DataToSend, 0, NrGot);

                            lock (IncomingData)
                            {
                                if (DebugLevel >= 2)
                                {
                                    string PushData = Encoding.UTF8.GetString(DataToSend);
                                    Log(2, "Push Data to XmlStream: {0}", PushData);
                                }
                                XMLStream.PushData(DataToSend);
                            }
                        }
                        while (NrGot == NrToGet && IsConnectedViaTLS);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Log(1, "OnDisconnect in PushData: {0}", e.ToString());
                        if (OnDisconnect == null)
                            OnDisconnect.Invoke();
                        return false;
                    }
                }
            }

            bool TextRead()
            {
                lock (Client)
                {
                    try
                    {
                        int Available = Client.Available;
                        if (Available == 0)
                            return false;

                        int NrToGet = IsConnectedViaTLS ? ReceiveBuffer.Length : Available;
                        NrToGet = Math.Min(NrToGet, ReceiveBuffer.Length);
                        int NrGot = 0;
                        do
                        {
                            NrGot = ActiveStream.Read(ReceiveBuffer, 0, NrToGet);
                            if (NrGot < 1)
                                break;
                            TotalDataReceivedInTextMode.Add(NrGot);

                            lock (IncomingData)
                            {
                                try
                                {
                                    string NewData = Encoding.UTF8.GetString(ReceiveBuffer, 0, NrGot);
                                    IncomingData += NewData;
                                    Log(2, "Adding incommingdata: {0}", NewData);
                                }
                                catch (System.Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION: " + ex.ToString());
                                }
                            }

                        } while (NrGot == NrToGet && IsConnectedViaTLS);

                        if (OnData != null)
                            OnData.Invoke();

                        return true;
                        //NewEvents.Enqueue(new Events(Events.EventType.GotData));
                    }
                    catch (Exception e)
                    {
                        Log(1, "OnDisconnect in TextRead: {0}", e.ToString());
                        if (OnDisconnect == null)
                            OnDisconnect.Invoke();
                        return false;
                    }
                }
            }

            void InnerXMLRead()
            {

                if (!XMLStream.HasData)
                    return;

                Log(3, "Innerxml read has data");

                XmlReaderSettings Settings = new XmlReaderSettings();
                Settings.ValidationType = ValidationType.None;
                Settings.ConformanceLevel = ConformanceLevel.Fragment;
                XmlReader Reader = XmlReader.Create(XMLStream, Settings);


                XElement RootNode = null;
                XElement CurrentElement = null;

                Log(2, "InnerXmlRead");

                while (Reader.Read())
                {
                    Log(2, "InnerXmlRead: NodeType: {0}, Name: {1}", Reader.NodeType, Reader.Name);
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

								XNamespace Namespace = Reader.NamespaceURI;
                                XElement NewElement = new XElement(Namespace + Reader.Name);
                                LoadAttributesFromReaderToElement(Reader, NewElement);

                                if (RootNode == null)
                                {
                                    RootNode = NewElement;
                                    CurrentElement = RootNode;

                                    if (SelfClosing)
                                    {
                                        if (DebugLevel >= 2)
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
                                    if (DebugLevel >= 2)
                                        Log(2, "Enqueing document: {0}", RootNode.ToString());
                                    TotalXmlDocumentsEnqueueud.Inc1();
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
                        InnerXMLRead();
                    }
                    catch (Exception e)
                    {
                        Log(1, "Exception in blockingxmlread: {0}", e.ToString());
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
                    bool ReadSomething = false;
                    switch (Mode)
                    {
                        case ReadMode.Text:
                            ReadSomething = TextRead();
                            break;
                        case ReadMode.XML:
                            ReadSomething = PushDataToXMLParserStream();
                            break;
                    }
                    if (!ReadSomething)
                        Thread.Sleep(1);
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
                    catch (System.Exception ex)
                    {
                        if (System.Diagnostics.Debugger.IsAttached)
                            System.Diagnostics.Debugger.Break();
                    }
                }
            }


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


            void SendKeepAliveWhitespace(object State)
            {
                if (LastSentDataToSocket > DateTime.UtcNow.AddSeconds(-KeepAliveTimerIntervalSeconds))
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


            public void WriteTextToSocket(string Data)
            {
                byte[] OutgoingBuffer = Encoding.UTF8.GetBytes(Data);
                lock (Client)
                {
                    try
                    {
                        TotalDataSent.Add(OutgoingBuffer.Length);
                        lock (ClientWriteLock)
                        {
                            LastSentDataToSocket = DateTime.UtcNow;
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
                }
            }

            #endregion
        }
    }
}

