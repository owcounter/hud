using Newtonsoft.Json;
using System.Net.Http;

namespace Owmeta.Authentication
{
    public class KeycloakAuth
    {
        private readonly string keycloakUrl = "https://id.owmeta.io";
        private readonly string realm = "owmeta";
        private readonly string clientId = "default-client";

        public async Task<KeycloakConnectOutput> Authenticate(string username, string password)
        {
            var tokenEndpoint = $"{keycloakUrl}/realms/{realm}/protocol/openid-connect/token";
            var formData = new Dictionary<string, string>
            {
                {"grant_type", "password"},
                {"client_id", clientId},
                {"username", username},
                {"password", password},
                {"scope", "openid offline_access"}
            };

            return await SendTokenRequest(tokenEndpoint, formData);
        }

        public async Task<KeycloakConnectOutput> RefreshToken(string refreshToken)
        {
            var tokenEndpoint = $"{keycloakUrl}/realms/{realm}/protocol/openid-connect/token";
            var formData = new Dictionary<string, string>
            {
                {"grant_type", "refresh_token"},
                {"client_id", clientId},
                {"refresh_token", refreshToken},
                {"scope", "openid offline_access"}
            };

            return await SendTokenRequest(tokenEndpoint, formData);
        }

        private async Task<KeycloakConnectOutput> SendTokenRequest(string endpoint, Dictionary<string, string> formData)
        {
            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(formData);
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await client.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<KeycloakConnectOutput>(responseContent);
                    return result ?? throw new Exception("Failed to deserialize response");
                }
                else
                {
                    throw new Exception($"Token request failed. Status code: {response.StatusCode}. Content: {responseContent}");
                }
            }
        }

        public async Task RevokeToken(string token)
        {
            var revokeEndpoint = $"{keycloakUrl}/realms/{realm}/protocol/openid-connect/revoke";
            var formData = new Dictionary<string, string>
            {
                {"client_id", clientId},
                {"token", token}
            };

            using (var client = new HttpClient())
            {
                var content = new FormUrlEncodedContent(formData);

                var response = await client.PostAsync(revokeEndpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Token revocation failed. Status code: {response.StatusCode}. Content: {responseContent}");
                }
            }
        }
    }

    public class KeycloakConnectOutput
    {
        public required string access_token { get; set; }
        public int expires_in { get; set; }
        public int refresh_expires_in { get; set; }
        public required string refresh_token { get; set; }
        public required string token_type { get; set; }
        public int not_before_policy { get; set; }
        public required string session_state { get; set; }
        public required string scope { get; set; }
    }
}