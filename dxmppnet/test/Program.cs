using System;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Linq;
using System.IO;
using System.Collections.Generic;

namespace Test
{
	class MainClass
	{
		class EchoBot
		{
			DXMPP.Connection Connection;

			public EchoBot()
			{
				// Chose to connect with Certificate (SASL External) or with password (SCRAM-SHA-1)
				X509Certificate2 Certificate = 
					new X509Certificate2("/home/stefan/src/certs/mycert.pfx", "", 
					                     X509KeyStorageFlags.MachineKeySet);

				/*
				Connection = new DXMPP.Connection("sarah", 5222, 
				                                  new DXMPP.JID("dxmpp@sarah/test"), "dxmpptest");
				*/

				Connection = new DXMPP.Connection("sarah", 5222, 
				                                  new DXMPP.JID("dxmpp@sarah/test"), 
				                                  "dxmpptest" /* Optional with certificate but then we cannot fall back to scram mechanism */, 
				                                  Certificate);

				Connection.AllowSelfSignedServerCertificate = true;

				Connection.OnStanzaMessage += HandleOnStanzaCallback;
                Connection.OnConnectionStateChanged += HandleOnConnectionStateChangedCallback;
				Connection.Connect();
			}

            void HandleOnConnectionStateChangedCallback (DXMPP.Connection.CallbackConnectionState NewState)
            {
                Console.WriteLine(NewState);
            }
                           

			void HandleOnStanzaCallback (DXMPP.StanzaMessage Data)
			{
				var BodyElement = Data.Payload.XPathSelectElement ("//body");
				if (BodyElement == null)
					return;

				DXMPP.StanzaMessage EchoMessage = new DXMPP.StanzaMessage ();
				EchoMessage.To = Data.From;
				EchoMessage.From = Data.To;
				EchoMessage.Payload.Add (BodyElement);
				Connection.SendStanza (EchoMessage);
			}
		}

       

		public static void Main (string[] args)
		{
			EchoBot T = new EchoBot ();
		}
	}
}
