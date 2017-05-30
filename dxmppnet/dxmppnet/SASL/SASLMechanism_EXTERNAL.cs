using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DXMPP
{
	namespace SASL
	{
		internal class SASL_Mechanism_EXTERNAL
			: SASLMechanism
		{
			public override void Begin()
			{
				//string Request = MyJID.GetBareJID();
				//Request = Convert.ToBase64String(Encoding.UTF8.GetBytes(Request));

				// Todo: Check if Subject alt name contains a jid or many, if so we need to set the jid here (Request) otherwise empty (=).
				string AuthXML = string.Empty;
				AuthXML += "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='EXTERNAL'>";
				AuthXML += "=";
				AuthXML += "</auth>";

				Uplink.WriteTextToSocket(AuthXML);
			}

			public override void Challenge(XElement Challenge)
			{
			}

			public override bool Verify(XElement SuccessTag)
			{
				return true;
			}

			public SASL_Mechanism_EXTERNAL(Network.AsyncTCPXMLClient Uplink,
				JID MyJID,
				X509Certificate Certificate)
				: base(Uplink, MyJID, Certificate)
			{				
			}
		}
	}	
}

