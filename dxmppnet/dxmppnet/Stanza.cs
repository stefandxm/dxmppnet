using System;
using System.Xml;
using System.Xml.Linq;

namespace DXMPP
{
	public class Stanza
	{
		public enum StanzaType
		{
			IQ,
			Message,
			Presence
		}

		public XElement Payload;
		public JID To;
		public JID From;
		public string ID;
		public StanzaType Type;

		public Stanza(XElement Payload)
		{
			this.Payload = Payload;

			if(Payload.Attribute("to") != null )
				To = new JID(Payload.Attribute("to").Value);

			if(Payload.Attribute("from") != null)
				From = new JID(Payload.Attribute("from").Value);

			if(Payload.Attribute("id") != null)
				this.ID = Payload.Attribute("id").Value;
		}

		public override string ToString()
		{
			return Payload.ToString(SaveOptions.DisableFormatting);
		}

		public virtual void EnforceAttributes(JID From)
		{			
			if(To != null)
				Payload.SetAttributeValue("to", To.ToString());

			if(From != null)
				Payload.SetAttributeValue("from", From.ToString());
			
			Payload.SetAttributeValue("id", ID);
		}

		public Stanza(StanzaType Type)
		{
			this.Type = Type;

			switch (this.Type)
			{
				case StanzaType.IQ:
					Payload = new XElement("iq");
					break;
				case StanzaType.Message:
					Payload = new XElement("message");
					break;
			}
			ID = Guid.NewGuid().ToString();
		}
	}

	public class StanzaMessage : Stanza
	{
		public enum StanzaMessageType
		{
			Chat,
			Error,
			Groupchat,
			Headline,
			Normal
		}

		public StanzaMessageType MessageType;

		public StanzaMessage(XElement Message)
			: base(Message)
		{
			switch (Message.Attribute ("type").Value) {
			case "error":
				this.MessageType = StanzaMessageType.Error;
				break;
			case "chat":
				this.MessageType = StanzaMessageType.Chat;
				break;
			case "groupchat":
				this.MessageType = StanzaMessageType.Groupchat;
				break;
			case "headline":
				this.MessageType = StanzaMessageType.Headline;
				break;
			case "normal":
				this.MessageType = StanzaMessageType.Normal;
				break;
			}
		}

		public override string ToString()
		{
			return base.ToString();
		}

		public override void EnforceAttributes(JID From)
		{
			base.EnforceAttributes(From);

			switch (MessageType) {
			case StanzaMessageType.Chat:
				Payload.SetAttributeValue ("type", "chat");
				break;
			case StanzaMessageType.Error:
				Payload.SetAttributeValue ("type", "error");
				break;
			}

		}

		public StanzaMessage ()
			: base(StanzaType.Message)
		{
			this.MessageType = StanzaMessageType.Chat;
		}
	}


	public class StanzaIQ : Stanza
	{
		public enum StanzaIQType
		{
			Get,
			Set,
			Result,
			Error
		}

		public StanzaIQType IQType;

		public StanzaIQ(XElement IQ)
			: base(IQ)
		{
		}

		public override string ToString()
		{
			return base.ToString();
		}

		public override void EnforceAttributes(JID From)
		{
			base.EnforceAttributes(From);

			switch (IQType)
			{
				case StanzaIQType.Get:
					Payload.SetAttributeValue("type", "get");
					break;
				case StanzaIQType.Set:
					Payload.SetAttributeValue("type", "set");
					break;
				case StanzaIQType.Result:
					Payload.SetAttributeValue("type", "result");
					break;
				case StanzaIQType.Error:
					Payload.SetAttributeValue("type", "error");
					break;
			}

		}

		public StanzaIQ(StanzaIQType Type)
			: base(StanzaType.IQ)
		{
			this.IQType = Type;
		}
	}

	public class StanzaPresence : Stanza
	{
		public StanzaPresence(XElement Presence)
			: base(Presence)
		{
		}

		public override string ToString()
		{
			return base.ToString();
		}

		public new void EnforceAttributes(JID From)
		{
			base.EnforceAttributes(From);

		}

		public StanzaPresence()
			: base(StanzaType.Presence)
		{
		}
	}
}

