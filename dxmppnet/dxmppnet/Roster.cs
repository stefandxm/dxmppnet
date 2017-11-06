using System;
using System.Linq;
using System.Xml;
using System.Xml.XPath;
using System.Xml.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace DXMPP
{
    public class Roster : IDisposable
	{
		public enum SubscribeResponse
		{
			Allow,
			AllowAndSubscribe,
			Reject
		}

		public delegate SubscribeResponse OnSubscribeCallback(JID From);
		public OnSubscribeCallback OnSubscribe;

		public delegate void OnUnSubscribedCallback(JID From);
		public OnUnSubscribedCallback OnUnsubscribed;

		public delegate void OnSubscribedCallback(JID To);
		public OnSubscribeCallback OnSubscribed;

		public delegate void OnPresenceCallback(JID From, bool Available, int Priority, string Status, string Show);
		public OnPresenceCallback OnPresence;

		public struct PresenceInformation
		{
			public int Priority;
			public JID FullJID;
			public string Show;
			public string Status;
		}

		public ConcurrentDictionary<string, List<PresenceInformation>> AggregatedPresence = 
			new ConcurrentDictionary<string, List<PresenceInformation>>();

		void PresenceHandler (XElement Node)
		{
            string type = "available";

            if (Node.Attribute("type") != null)
            {
                type = Node.Attribute ("type").Value;
            }

			switch (type) {
			case "subscribe":
				HandleSubscribe (Node);
				return;
			case "subscribed":
				HandleSubscribed (Node);
				return;
			case "unsubscribe":
				HandleUnsubscribe (Node);
				return;
			case "unsubscribed":
				HandleUnsubscribed (Node);
				return;

			}
			JID From = new JID (Node.Attribute ("from").Value);
			int Priority = 0;
			XElement PrioNode = Node.XPathSelectElement ("//priority");
			if (PrioNode != null)
				Priority = Convert.ToInt32 (PrioNode.Value);

			string Show = string.Empty;
			string Status = string.Empty;

			XElement ShowNode = Node.XPathSelectElement ("//show");
			if (ShowNode != null)
				Show = ShowNode.Value;
			XElement StatusNode = Node.XPathSelectElement ("//status");
            if (StatusNode != null)
				Show = StatusNode.Value;

			PresenceInformation PI = new PresenceInformation()
			{
				FullJID = From,
				Priority = Priority,
				Show = Show,
				Status = Status
			};

			if (!AggregatedPresence.ContainsKey(From.GetBareJID()))
			{
				AggregatedPresence[From.GetBareJID()] = new List<PresenceInformation>();
			}
		    AggregatedPresence[From.GetBareJID()].Add(PI);

			if (OnPresence != null)
				OnPresence.Invoke (From, Status != "unavailable", Priority, Status, Show);
		}


		void HandleSubscribe(XElement Node)
		{
			if (OnSubscribe == null)
				return;

			JID From = new JID (Node.Attribute ("from").Value);
			SubscribeResponse Action = OnSubscribe.Invoke (From);
			if (Action == SubscribeResponse.Reject)
				return;

			XElement PresenceTag = new XElement ("presence");
			PresenceTag.SetAttributeValue("to", From.GetBareJID());
			PresenceTag.SetAttributeValue("type", "subscribed");
			Uplink.GetNetworkClient().WriteTextToSocket(PresenceTag.ToString());

			if (Action == SubscribeResponse.Allow)
				return;

			Subscribe (From);
		}

		void HandleSubscribed(XElement Node)
		{
			if (OnSubscribed == null)
				return;

			JID From = new JID (Node.Attribute ("from").Value);
			OnSubscribed.Invoke (From);
		}

		void HandleError(XElement Node)
		{
			// Throw ?
		}

		void HandleUnsubscribe(XElement Node)
		{
			if (OnUnsubscribed == null)
				return;

			JID From = new JID (Node.Attribute ("from").Value);
			OnUnsubscribed.Invoke (From);
		}

		void HandleUnsubscribed(XElement Node)
		{
			if (OnUnsubscribed == null)
				return;

			JID From = new JID (Node.Attribute ("from").Value);
			OnUnsubscribed.Invoke (From);
		}

		public void Subscribe(JID To)
		{
			XElement PresenceTag = new XElement ("presence");

			PresenceTag.SetAttributeValue("to", To.GetBareJID());
			PresenceTag.SetAttributeValue("type", "subscribe");
			Uplink.GetNetworkClient().WriteTextToSocket(PresenceTag.ToString());
		}

		public void Unsubscribe(JID To)
		{
			XElement PresenceTag = new XElement("presence");
			PresenceTag.SetAttributeValue("to", To.GetBareJID());
			PresenceTag.SetAttributeValue("type", "unsubscribe");
			Uplink.GetNetworkClient().WriteTextToSocket(PresenceTag.ToString());		}

		private Connection Uplink;

		public Roster (Connection Uplink)
		{
			this.Uplink = Uplink;
			Uplink.OnPresence += PresenceHandler;
		}

        #region IDisposable implementation

        public void Dispose()
        {
            Uplink.OnPresence -= PresenceHandler;
        }

        #endregion
	}
}

