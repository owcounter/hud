using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Owmeta.Authentication;
using Owmeta.Model;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Owmeta.Services
{
    public class ApiService : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly KeycloakAuth keycloakAuth;
        private readonly string tokenFileName = "owmeta_oauth_token.json";
        private string? accessToken;
        private string? refreshToken;
        private readonly SemaphoreSlim tokenRefreshSemaphore = new SemaphoreSlim(1, 1);
        private DateTime? tokenExpiryTime;
        private Timer? _tokenRefreshTimer;
        private bool _disposed;
        private bool _sessionExpiredFired;

        // Event fired when session expires and re-login is required
        public event EventHandler? SessionExpired;

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
                {
                    StartTokenRefreshTimer();
                    return true;
                }

                // If validation failed, try refreshing
                var refreshResult = await RefreshAndValidateToken();
                if (refreshResult)
                {
                    StartTokenRefreshTimer();
                }
                return refreshResult;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading or validating tokens: {ex.Message}");
                DeleteTokenFile();
                return false;
            }
        }

        private void StartTokenRefreshTimer()
        {
            StopTokenRefreshTimer();
            _sessionExpiredFired = false;

            // Refresh tokens every 5 minutes to keep them fresh
            const int refreshIntervalMs = 5 * 60 * 1000; // 5 minutes
            _tokenRefreshTimer = new Timer(OnTokenRefreshTimerTick, null, refreshIntervalMs, refreshIntervalMs);
            Logger.Log("Token refresh timer started (5 min interval)");
        }

        private void OnTokenRefreshTimerTick(object? state)
        {
            // Run async work on thread pool with proper exception handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await BackgroundTokenRefresh();
                }
                catch (Exception ex)
                {
                    Logger.Log($"Timer callback error: {ex.Message}");
                }
            });
        }

        private void StopTokenRefreshTimer()
        {
            _tokenRefreshTimer?.Dispose();
            _tokenRefreshTimer = null;
        }

        private async Task BackgroundTokenRefresh()
        {
            // Check if already disposed or session already expired
            if (_disposed || _sessionExpiredFired) return;

            try
            {
                Logger.Log("Background token refresh starting...");

                if (IsTokenExpiredOrExpiringSoon(60)) // Refresh if expiring within 60 seconds
                {
                    var success = await RefreshAndValidateToken();
                    if (success)
                    {
                        Logger.Log("Background token refresh successful");
                    }
                    else
                    {
                        Logger.Log("Background token refresh failed - session expired");
                        FireSessionExpired();
                    }
                }
                else
                {
                    Logger.Log("Token still valid, skipping refresh");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Background token refresh error: {ex.Message}");
            }
        }

        private void FireSessionExpired()
        {
            if (_sessionExpiredFired) return;
            _sessionExpiredFired = true;
            StopTokenRefreshTimer();
            SessionExpired?.Invoke(this, EventArgs.Empty);
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
            if (string.IsNullOrEmpty(accessToken))
            {
                FireSessionExpired();
                throw new UnauthorizedException("No access token available");
            }

            // Pre-emptively refresh token if it's about to expire
            if (IsTokenExpiredOrExpiringSoon())
            {
                var refreshed = await RefreshAndValidateToken();
                if (!refreshed)
                {
                    Logger.Log("Token refresh failed before API call - session expired");
                    FireSessionExpired();
                    throw new UnauthorizedException("Session expired - please login again");
                }
            }

            return await SendScreenshotRequest(screenshotBase64, retryOnUnauthorized: true);
        }

        private async Task<ScreenshotProcessingResponse?> SendScreenshotRequest(string screenshotBase64, bool retryOnUnauthorized)
        {
            string formattedBase64 = $"data:image/jpeg;base64,{screenshotBase64}";
            var input = new { screenshot_base64 = formattedBase64, use_websocket = false };
            var content = new StringContent(JsonConvert.SerializeObject(input), System.Text.Encoding.UTF8, "application/json");

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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

            // Handle 401/token expired with retry
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                errorContent.Contains("Token is expired", StringComparison.OrdinalIgnoreCase))
            {
                if (retryOnUnauthorized)
                {
                    Logger.Log("Token expired during request - attempting refresh and retry");
                    var refreshed = await RefreshAndValidateToken();
                    if (refreshed)
                    {
                        Logger.Log("Token refreshed successfully - retrying request");
                        return await SendScreenshotRequest(screenshotBase64, retryOnUnauthorized: false);
                    }
                }

                // Refresh failed or already retried - session is expired
                Logger.Log("Session expired - login required");
                FireSessionExpired();
                throw new UnauthorizedException("Session expired - please login again");
            }

            throw new ApiException($"API request failed: {response.StatusCode}");
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
                StopTokenRefreshTimer();
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopTokenRefreshTimer();
            tokenRefreshSemaphore.Dispose();
            httpClient.Dispose();
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