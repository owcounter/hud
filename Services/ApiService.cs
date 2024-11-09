using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Owcounter.Authentication;
using Owcounter.Model;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Owcounter.Services
{
    public class ApiService
    {
        private readonly HttpClient httpClient;
        private readonly KeycloakAuth keycloakAuth;
        private readonly string tokenFileName = "owcounter_oauth_token.json";
        private string? accessToken;
        private string? refreshToken;

        public ApiService(string apiBaseUrl, KeycloakAuth keycloakAuth)
        {
            httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
            this.keycloakAuth = keycloakAuth;
        }

        public async Task<bool> LoadAndValidateTokens()
        {
            if (!System.IO.File.Exists(tokenFileName))
                return false;

            try
            {
                var tokenJson = System.IO.File.ReadAllText(tokenFileName);
                var tokenResponse = JsonConvert.DeserializeObject<KeycloakConnectOutput>(tokenJson);
                if (tokenResponse == null)
                {
                    return false;
                }

                accessToken = tokenResponse.access_token;
                refreshToken = tokenResponse.refresh_token;

                if (!string.IsNullOrEmpty(accessToken) && await ValidateToken(accessToken))
                    return true;

                if (await RefreshAndValidateToken())
                    return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading or validating tokens: {ex.Message}");
            }

            DeleteTokenFile();
            return false;
        }

        private async Task<bool> ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            try
            {
                var response = await httpClient.GetAsync("/user/session");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Log($"Token validation failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RefreshAndValidateToken()
        {
            try
            {
                if (string.IsNullOrEmpty(refreshToken))
                    return false;

                var tokenResponse = await keycloakAuth.RefreshToken(refreshToken);
                accessToken = tokenResponse.access_token;
                refreshToken = tokenResponse.refresh_token;

                System.IO.File.WriteAllText(tokenFileName, JsonConvert.SerializeObject(tokenResponse));

                return !string.IsNullOrEmpty(accessToken) && await ValidateToken(accessToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to refresh token: {ex.Message}");
                return false;
            }
        }

        public async Task<ScreenshotProcessingResponse?> SendScreenshotToServer(string screenshotBase64)
        {
            string formattedBase64 = $"data:image/jpeg;base64,{screenshotBase64}";
            var input = new { screenshot_base64 = formattedBase64, use_websocket = false };
            var content = new StringContent(JsonConvert.SerializeObject(input), System.Text.Encoding.UTF8, "application/json");

            return await SendApiRequestWithRetry(async () =>
            {
                var response = await httpClient.PostAsync("/process-screenshot", content);
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var settings = new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter>
                        {
                            new StringEnumConverter(),
                            new ScreenshotProcessingResponseConverter()
                        }
                    };

                    var result = JsonConvert.DeserializeObject<ScreenshotProcessingResponse>(jsonResponse, settings);
                    if (result == null)
                    {
                        throw new ApiException("Failed to deserialize screenshot processing response");
                    }
                    return result;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                Logger.Log($"API request failed: {response.StatusCode} - {errorContent}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new UnauthorizedException("Token expired");
                }

                throw new ApiException($"API request failed: {response.StatusCode}");
            });
        }

        private async Task<T?> SendApiRequestWithRetry<T>(Func<Task<T>> apiCall)
        {
            int maxRetries = 2;
            for (int i = 0; i <= maxRetries; i++)
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new UnauthorizedException("No access token available");
                }

                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                try
                {
                    return await apiCall();
                }
                catch (UnauthorizedException)
                {
                    if (i < maxRetries && await RefreshAndValidateToken())
                    {
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Logger.Log($"HTTP Request failed: {e.Message}");
                }

                if (i == maxRetries)
                {
                    throw new ApiException("Failed to process request after max retries");
                }
            }

            throw new ApiException("Unexpected error in request retry loop");
        }

        private void DeleteTokenFile()
        {
            if (System.IO.File.Exists(tokenFileName))
            {
                System.IO.File.Delete(tokenFileName);
            }
        }

        public async Task Logout()
        {
            try
            {
                if (!string.IsNullOrEmpty(accessToken))
                    await keycloakAuth.RevokeToken(accessToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during logout: {ex.Message}");
            }
            finally
            {
                DeleteTokenFile();
                accessToken = null;
                refreshToken = null;
            }
        }

        private class UnauthorizedException : Exception
        {
            public UnauthorizedException(string message) : base(message) { }
        }

        private class ApiException : Exception
        {
            public ApiException(string message) : base(message) { }
        }
    }
}