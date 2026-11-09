using System;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class NintendoSwitchTests
    {
        [Test]
        public void Should_Generate_Stable_Negative_AppId()
        {
            int id1 = NintendoSwitchClient.GetStableSwitchAppId("0100152000022000", "Super Mario Odyssey");
            int id2 = NintendoSwitchClient.GetStableSwitchAppId("0100152000022000", "Super Mario Odyssey");
            int id3 = NintendoSwitchClient.GetStableSwitchAppId("0100000000010000", "Super Mario Odyssey");

            // Must be strictly negative
            Assert.IsTrue(id1 < 0);
            Assert.IsTrue(id1 <= -1000);

            // Must be deterministic
            Assert.AreEqual(id1, id2);

            // Different title IDs produce different IDs
            Assert.AreNotEqual(id1, id3);
        }

        [Test]
        public void Should_Extract_Session_Token_Code_From_Auth_Url()
        {
            string url1 = "npf54789befb391a838://auth#session_token_code=my_secret_token_code_123&state=xyz";
            Assert.AreEqual("my_secret_token_code_123", NintendoSwitchClient.ExtractSessionTokenCode(url1));

            string url2 = "https://example.com/oauth?session_token_code=another_code_456&param=1";
            Assert.AreEqual("another_code_456", NintendoSwitchClient.ExtractSessionTokenCode(url2));

            string rawCode = "direct_token_code_789";
            Assert.AreEqual("direct_token_code_789", NintendoSwitchClient.ExtractSessionTokenCode(rawCode));

            Assert.IsNull(NintendoSwitchClient.ExtractSessionTokenCode("https://accounts.nintendo.com/error?no_code=1"));
            Assert.IsNull(NintendoSwitchClient.ExtractSessionTokenCode(""));
            Assert.IsNull(NintendoSwitchClient.ExtractSessionTokenCode(null));
        }

        [Test]
        public void Should_Create_Valid_Login_Challenge()
        {
            var challenge = NintendoSwitchClient.CreateLoginChallenge(false);
            Assert.IsNotNull(challenge);
            Assert.AreEqual(NintendoSwitchClient.ParentalClientId, challenge.ClientId);
            Assert.IsNotNull(challenge.CodeVerifier);
            Assert.AreEqual(50, challenge.CodeVerifier.Length);
            Assert.IsNotNull(challenge.State);
            Assert.AreEqual(50, challenge.State.Length);
            Assert.Contains("client_id=" + NintendoSwitchClient.ParentalClientId, challenge.LoginUrl);
            Assert.Contains("session_token_code_challenge_method=S256", challenge.LoginUrl);

            var storeChallenge = NintendoSwitchClient.CreateLoginChallenge(true);
            Assert.IsNotNull(storeChallenge);
            Assert.AreEqual(NintendoSwitchClient.StoreClientId, storeChallenge.ClientId);
            Assert.Contains("client_id=" + NintendoSwitchClient.StoreClientId, storeChallenge.LoginUrl);
        }
    }
}
