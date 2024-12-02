using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Owmeta.Authentication;
using Owmeta.Model;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Owmeta.Services
{
    public class ApiService
    {
        private readonly HttpClient httpClient;
        private readonly KeycloakAuth keycloakAuth;
        private readonly string tokenFileName = "owmeta_oauth_token.json";
        private string? accessToken;
        private string? refreshToken;
        private readonly SemaphoreSlim tokenRefreshSemaphore = new SemaphoreSlim(1, 1);
        private DateTime? tokenExpiryTime;

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
                    return false;

                accessToken = tokenResponse.access_token;
                refreshToken = tokenResponse.refresh_token;
                UpdateTokenExpiryTime(tokenResponse.expires_in);

                // If token is expired or about to expire, try refreshing
                if (IsTokenExpiredOrExpiringSoon())
                {
                    return await RefreshAndValidateToken();
                }

                // Validate the current token
                if (!string.IsNullOrEmpty(accessToken) && await ValidateToken(accessToken))
                    return true;

                // If validation failed, try refreshing
                return await RefreshAndValidateToken();
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading or validating tokens: {ex.Message}");
                DeleteTokenFile();
                return false;
            }
        }

        private bool IsTokenExpiredOrExpiringSoon(int bufferSeconds = 30)
        {
            return tokenExpiryTime == null || DateTime.UtcNow.AddSeconds(bufferSeconds) >= tokenExpiryTime;
        }

        private void UpdateTokenExpiryTime(int expiresIn)
        {
            tokenExpiryTime = DateTime.UtcNow.AddSeconds(expiresIn);
        }

        private async Task<bool> ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/user/session");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await httpClient.SendAsync(request);

                // Consider 401/403 as validation failures but don't log them as errors
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return false;
                }

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
            await tokenRefreshSemaphore.WaitAsync();
            try
            {
                // Add additional check for refresh token expiration
                if (string.IsNullOrEmpty(refreshToken))
                {
                    Logger.Log("No refresh token available");
                    return false;
                }

                try
                {
                    var tokenResponse = await keycloakAuth.RefreshToken(refreshToken);
                    if (tokenResponse == null)
                    {
                        Logger.Log("Received null token response during refresh");
                        return false;
                    }

                    accessToken = tokenResponse.access_token;
                    refreshToken = tokenResponse.refresh_token;
                    UpdateTokenExpiryTime(tokenResponse.expires_in);

                    // Save the new tokens
                    System.IO.File.WriteAllText(tokenFileName, JsonConvert.SerializeObject(tokenResponse));

                    // Validate the new token immediately
                    if (!string.IsNullOrEmpty(accessToken) && await ValidateToken(accessToken))
                    {
                        return true;
                    }

                    Logger.Log("New token validation failed after refresh");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Log($"Token refresh failed with error: {ex.Message}");
                    DeleteTokenFile();
                    return false;
                }
            }
            finally
            {
                tokenRefreshSemaphore.Release();
            }
        }

        public async Task<ScreenshotProcessingResponse?> SendScreenshotToServer(string screenshotBase64)
        {
            string formattedBase64 = $"data:image/jpeg;base64,{screenshotBase64}";
            var input = new { screenshot_base64 = formattedBase64, use_websocket = false };
            var content = new StringContent(JsonConvert.SerializeObject(input), System.Text.Encoding.UTF8, "application/json");

            return await SendApiRequestWithRetry(async () =>
            {
                // Pre-emptively refresh token if it's about to expire
                if (IsTokenExpiredOrExpiringSoon())
                {
                    await RefreshAndValidateToken();
                }

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
            int maxRetries = 3;
            int currentRetry = 0;
            bool tokenRefreshed = false;

            while (currentRetry <= maxRetries)
            {
                if (string.IsNullOrEmpty(accessToken))
                {
                    throw new UnauthorizedException("No access token available");
                }

                // Proactively refresh token if it's about to expire
                if (IsTokenExpiredOrExpiringSoon() && !tokenRefreshed)
                {
                    Logger.Log("Token expired or expiring soon, attempting refresh");
                    if (await RefreshAndValidateToken())
                    {
                        tokenRefreshed = true;
                    }
                    else
                    {
                        throw new UnauthorizedException("Failed to refresh expired token");
                    }
                }

                try
                {
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    return await apiCall();
                }
                catch (UnauthorizedException)
                {
                    if (currentRetry < maxRetries && !tokenRefreshed && await RefreshAndValidateToken())
                    {
                        tokenRefreshed = true;
                        currentRetry++;
                        continue;
                    }
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log($"API request failed: {e.Message}");
                    if (currentRetry == maxRetries)
                    {
                        throw new ApiException($"Failed to process request after {maxRetries} retries");
                    }
                    currentRetry++;
                    await Task.Delay(1000 * currentRetry); // Exponential backoff
                }
            }

            throw new ApiException("Unexpected error in request retry loop");
        }

        private void DeleteTokenFile()
        {
            try
            {
                if (System.IO.File.Exists(tokenFileName))
                {
                    System.IO.File.Delete(tokenFileName);
                }
                accessToken = null;
                refreshToken = null;
                tokenExpiryTime = null;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error deleting token file: {ex.Message}");
            }
        }

        public async Task Logout()
        {
            try
            {
                if (!string.IsNullOrEmpty(accessToken))
                {
                    await keycloakAuth.RevokeToken(accessToken);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error during logout: {ex.Message}");
            }
            finally
            {
                DeleteTokenFile();
            }
        }
    }

    public class ApiException : Exception
    {
        public ApiException(string message) : base(message) { }
    }

    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message) { }
    }
}