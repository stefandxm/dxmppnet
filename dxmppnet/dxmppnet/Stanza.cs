using System;
using System.Xml;
using System.Xml.Linq;

namespace DXMPP
{
	public class Stanza
	{
		public enum StanzaType
		{
			Chat,
			Error
		}

		public XElement Message;
		public JID To;
		public JID From;
		public string ID;
		public StanzaType Type;

		public Stanza(XElement Message)
		{
			this.Message = Message;
			To = new JID (Message.Attribute ("to").Value);
			From = new JID (Message.Attribute ("from").Value);
			this.ID = Message.Attribute ("id").Value;

			switch (Message.Attribute ("type").Value) {
			case "error":
				this.Type = StanzaType.Error;
				break;
			case "chat":
				this.Type = StanzaType.Chat;
				break;
			}
		}

		public override string ToString()
		{
			return Message.ToString ( SaveOptions.DisableFormatting);
		}

		public void EnforceAttributes(JID From)
		{
			switch (Type) {
			case StanzaType.Chat:
				Message.SetAttributeValue ("type", "chat");
				break;
			case StanzaType.Error:
				Message.SetAttributeValue ("type", "error");
				break;
			}

			Message.SetAttributeValue ("to", To.ToString ());
			Message.SetAttributeValue ("from", From.ToString ());
			Message.SetAttributeValue ("id", ID);
		}

		public Stanza ()
		{
			this.Type = StanzaType.Chat;

			Message = new XElement ("message");
			ID = Guid.NewGuid ().ToString();
		}
	}
}

