using System;

namespace DXMPP
{
	public class JID
	{
		string Username;
		string Domain;
		string Resource;

		void LoadFromString(string Data)
		{
			int IndexOfSlash = Data.IndexOf ('/');
			if (IndexOfSlash != -1)
				Resource = Data.Substring (IndexOfSlash + 1);

			int IndexOfAt = Data.IndexOf ('@');
			if (IndexOfAt != -1) {
				Username = Data.Substring (0, IndexOfAt);
				IndexOfAt++;
			} else
				IndexOfAt = 0;
			if(IndexOfSlash > -1)
				Domain = Data.Substring (IndexOfAt, IndexOfSlash - IndexOfAt);
			else
				Domain = Data.Substring (IndexOfAt);
		}

		public JID ()
		{
		}

		public JID (string FullJID)
		{
			LoadFromString (FullJID);
		}

		public JID (string Username, string Domain, string Resource)
		{
			this.Username = Username;
			this.Domain = Domain;
			this.Resource = Resource;
		}

		public JID (string Bare, string Resource)
		{
			LoadFromString (Bare);
			SetResource (Resource);
		}

		public void SetDomain(string Domain)
		{
            this.Domain = Domain;
		}

        public void SetUsername(string Username)
        {
            this.Username = Username;
        }


        public void SetResource(string Resource)
        {
            this.Resource = Resource;
        }


        public string GetFullJID()
		{
			if (string.IsNullOrEmpty (Resource) 
				&& string.IsNullOrEmpty (Username))
				return GetDomain();

			if (string.IsNullOrEmpty (Resource))
				return GetUsername() + "@" + GetDomain();

			if (string.IsNullOrEmpty (Username))
				return GetDomain() + "/" + GetResource();

			return GetUsername() + "@" + GetDomain() + "/" + GetResource();
		}

		public string GetBareJID()
		{
			if (string.IsNullOrEmpty (Username)) {
				if (string.IsNullOrEmpty (Domain))
					return string.Empty;

				return Domain;
			}



			return Username + "@" + Domain;
		}

		public string GetUsername()
		{
			if (Username == null)
				return string.Empty;

			return Username;
		}

		public string GetDomain()
		{
			if (Domain == null)
				return string.Empty;

			return Domain;
		}

		public string GetResource()
		{
			if (Resource == null)
				return string.Empty;

			return Resource;
		}

		public override string ToString ()
		{
			return GetFullJID ();
		}
	}
}

