using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace DXMPP
{
	namespace SASL
	{
		internal class SASL_Mechanism_SCRAM_SHA1
			: SASLMechanism
		{
			static void memcpy(byte[] output, int outputoffset, byte[] input, int nrbytes)
			{
				for (int i = 0; i < nrbytes; i++)
				{
					output[i + outputoffset] = input[i];
				}
			}
            string ServerProof = string.Empty;

			public override void Begin()
			{
				SelectedNounce = "arne"; // Guid.NewGuid().ToString();
                string Request = "n,,n=" + MyJID.GetUsername() + ",r=" + SelectedNounce;

                /*char[] outArray = new char[1024];
				int nrTotal = Convert.ToBase64CharArray(tempbuff, 0, offset, outArray, 0);
				string EncodedResponse = new string(outArray, 0, nrTotal);*/

                Request = Convert.ToBase64String(Encoding.UTF8.GetBytes((Request)),
                                                 Base64FormattingOptions.None);

				string AuthXML = string.Empty;
				AuthXML += "<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='SCRAM-SHA-1'>";// << std::endl;
				AuthXML += Request;
				AuthXML += "</auth>";

				Uplink.WriteTextToSocket(AuthXML);
			}

			public override void Challenge(XElement Challenge)
			{
				//string ChallengeBase64 = "cj1hcm5lV21qejZsQkI5ZmxjUWtld09rWnNYQT09LHM9ZEVPaUhHaC9KRVRIc2xKcGZVY2FBZz09LGk9NDA5Ng=="; //Challenge.Value;
				string ChallengeBase64 = Challenge.Value;
                string DecodedChallenge = Encoding.UTF8.GetString(
                    Convert.FromBase64String(ChallengeBase64));                

                string[] SplittedChallenge = DecodedChallenge.Split(',');

                Dictionary<string, string> ChallengeMap = new Dictionary<string, string>();
                foreach (string str in SplittedChallenge)
                {
                    string[] SplittedSplit = str.Split(new char[] { '=' }, 2);
                    ChallengeMap[SplittedSplit[0]] = SplittedSplit[1];
                }
                string r = ChallengeMap["r"];
                string s = ChallengeMap["s"];
                string i = ChallengeMap["i"];

                int NrNrIterations = Convert.ToInt32(i);
                string n = MyJID.GetUsername();
                byte[] SaltBytes = new byte[1024];
                byte[] DecodedS = Convert.FromBase64String(s);
                memcpy(SaltBytes, 0, DecodedS, DecodedS.Length);
                SaltBytes[DecodedS.Length] = 0;
                SaltBytes[DecodedS.Length+1] = 0;
                SaltBytes[DecodedS.Length+2] = 0;
                SaltBytes[DecodedS.Length+3] = 1;

                string c = "biws";


                HMACSHA1 hmacFromPassword = new HMACSHA1(Encoding.UTF8.GetBytes(Password));
                byte[] Result = hmacFromPassword.ComputeHash(SaltBytes, 0, DecodedS.Length+4);
                byte[] Previous = (byte[])Result.Clone();

                for (int j = 1; j < NrNrIterations; j++)
                {
                    byte[] tmp = hmacFromPassword.ComputeHash(Previous);
                    for (int k = 0; k < tmp.Length; k++)
                    {
                        Result[k] = (byte)( Result[k] ^ tmp[k] );
                        Previous[k] = tmp[k];
                    }
                }
                HMACSHA1 hmacFromSaltedPassword = new HMACSHA1(Result);

                byte[] ClientKey = hmacFromSaltedPassword.ComputeHash(Encoding.UTF8.GetBytes("Client Key"));
                SHA1 hash = SHA1.Create();
                byte[] StoredKey = hash.ComputeHash(ClientKey);

                string AuthMessage = string.Format("n={0},r={1},{2},c={3},r={4}", 
                                                   n, 
                                                   SelectedNounce, 
                                                   DecodedChallenge, 
                                                   c, 
                                                   r);


                HMACSHA1 hmacFromStoredKey = new HMACSHA1(StoredKey);
                byte[] ClientSignature = hmacFromStoredKey.ComputeHash(Encoding.UTF8.GetBytes(AuthMessage));
                byte[] ClientProof = new byte[ClientSignature.Length];

				for (int k = 0; k < ClientSignature.Length; k++)
				{
					ClientProof[k] = (byte) (ClientKey[k] ^ ClientSignature[k]);
				}

                byte[] ServerKey = hmacFromSaltedPassword.ComputeHash(Encoding.UTF8.GetBytes("Server Key"));

                HMACSHA1 hmacFromServerKey = new HMACSHA1(ServerKey);
                byte[] ServerSignature = hmacFromServerKey.ComputeHash( Encoding.UTF8.GetBytes( AuthMessage) );
                ServerProof = Convert.ToBase64String(ServerSignature, Base64FormattingOptions.None);

                string proof = Convert.ToBase64String(ClientProof, Base64FormattingOptions.None);
                string InnerResponse = string.Format("c={0},r={1},p={2}", c, r, proof);
                string Base64InnerResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(InnerResponse));
                string Response = string.Format("<response xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>{0}</response>", Base64InnerResponse);
                Uplink.WriteTextToSocket(Response);
			}

			public override bool Verify(XElement SuccessTag)
			{
                //Console.WriteLine("Got verify");
                string SuccessVal = SuccessTag.Value;
                string DecodedSuccess = Encoding.UTF8.GetString( Convert.FromBase64String(SuccessVal) );
                string ServerProofValidResponse = "v=" + ServerProof;
				return DecodedSuccess == ServerProofValidResponse;
			}

			public SASL_Mechanism_SCRAM_SHA1(Network.AsyncTCPXMLClient Uplink,
				JID MyJID,
				string Password)
				: base(Uplink, MyJID, Password)
			{
			}
		}
	}	
}

