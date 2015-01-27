using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text;

namespace DXMPP
{
	namespace SASL
	{
		namespace Weak
		{
			internal class SASLMechanism_PLAIN 
				: SASLMechanism
			{
				static void memcpy(byte [] output, int outputoffset, byte[] input, int nrbytes)
				{
					for( int i = 0; i < nrbytes;i++)
					{
						output[i+outputoffset] = input[i];
					}
				}
				public override void Begin()
				{
					string ManagedString_authid = MyJID.GetUsername();
					string ManagedString_authzid = "";

					byte [] tempbuff = new byte[1024];
					int offset= 0;
					byte[] authzid = Encoding.UTF8.GetBytes (ManagedString_authzid);
					byte[] authid = Encoding.UTF8.GetBytes (ManagedString_authid);
					byte[] password = Encoding.UTF8.GetBytes (Password);

					memcpy (tempbuff, offset, authzid, authzid.Length);

					offset+=authzid.Length;
					tempbuff[offset++]=0;

					memcpy (tempbuff, offset, authid, authid.Length);
					offset+=authid.Length;
					tempbuff[offset++]=0;

					memcpy(tempbuff, offset, password, password.Length);
					offset+= password.Length;

					char[] outArray = new char[1024];
					int nrTotal = Convert.ToBase64CharArray (tempbuff, 0, offset, outArray, 0);
					string EncodedResponse = new string(outArray, 0, nrTotal);

					string AuthXML = string.Empty;
					AuthXML += "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>";// << std::endl;
					AuthXML += EncodedResponse;
					AuthXML += "</auth>";

					//std::string blaha = AuthXML.str();
					Uplink.WriteTextToSocket (AuthXML);
				}

				public override void Challenge(XElement Challenge)
				{
				}

				public override bool Verify(XElement SuccessTag)
				{
					return true;
				}

				public SASLMechanism_PLAIN(Network.AsyncTCPXMLClient Uplink,
					JID MyJID, 
					string Password)
					:base( Uplink, MyJID, Password)
				{
				}
			}
		}
	}
}

