using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using telldusmqtt.Models;

namespace telldusmqtt.Services;

internal class TelldusClientService : ITelldusClientService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelldusClientService> _logger;
    private bool _disposedValue;
    private readonly TelldusSettings _telldusSettings;
    private Sensors? _sensors;
    private readonly string _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelldusMQTT");
    private string _accessToken = string.Empty;
    //private string _accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCIsImF1ZCI6Ik15RG90TmV0QXBwIiwiZXhwIjoxODE2MTc4NjkxfQ.eyJyZW5ldyI6dHJ1ZSwidHRsIjozMTUzNjAwMH0.zhp-wL-YQ4t7vO41OkwHyDW5rYpXR3A_vZ_SMYhZfSo";

    public TelldusClientService(ILogger<TelldusClientService> logger, IOptions<TelldusSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _telldusSettings = settings.Value;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TelldusClientService is starting...");
        await GetAccessTokenAsync(cancellationToken);
        var sensorsResult = await GetSensorsAsync(cancellationToken).ConfigureAwait(false);
        if (!sensorsResult)
        {
            _logger.LogError("Failed to retrieve sensors. TelldusClientService startup aborted.");
            throw new InvalidOperationException("Failed to retrieve sensors during startup.");
        }
        await GetSensorDataAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("TelldusClientService has started.");
    }


    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TelldusClientService is stopping...");
        // TODO: Implement stop logic
        _logger.LogInformation("TelldusClientService has stopped.");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async Task<Sensors> GetSensorsStatusAsync(CancellationToken cancellationToken)
    {
        await GetSensorDataAsync(cancellationToken).ConfigureAwait(false);
        return _sensors ?? new Sensors { Sensor = new List<Sensor>() };
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                _httpClient.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposedValue = true;
        }
    }

    private async Task<bool> GetSensorsAsync(CancellationToken cancellationToken)
    {
        string endpoint = $"http://{_telldusSettings.Host}/api/sensors/list";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        // Pass the token in the Bearer authorization header
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Successfully retrieved sensors from Telldus API.");
            var content = await response.Content.ReadAsStringAsync();
            try
            {
                _sensors = JsonSerializer.Deserialize<Sensors>(content);
                if (_sensors == null)
                {
                    _logger.LogError("Deserialization of sensors returned null.");
                    return false;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize sensors from Telldus API response.");
                return false;
            }
            return true;
        }
        else
        {
            _logger.LogError("Failed to retrieve sensors from Telldus API. Status Code: {StatusCode}, Reason: {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
            return false;
        }
    }

    private async Task GetSensorDataAsync(CancellationToken cancellationToken)
    {
        if (_sensors == null || _sensors.Sensor == null || _sensors.Sensor.Count == 0)
        {
            _logger.LogWarning("No sensors available to retrieve data for.");
            return;
        }
        // Parallelize sensor data retrieval with bounded concurrency to avoid N+1 sequential pattern
        var semaphore = new SemaphoreSlim(5); // Limit concurrency to 5, adjust as needed
        var tasks = new List<Task>();
        foreach (var sensor in _sensors.Sensor)
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    string endpoint = $"http://{_telldusSettings.Host}/api/sensor/info";
                    var queryParams = new Dictionary<string, string?>
                    {
                        {"id", sensor.Id.ToString()  },
                    };
                    string fullUrl = QueryHelpers.AddQueryString(endpoint, queryParams);
                    using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                    request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("Successfully retrieved data for sensor {SensorId} with name {SensorName} from Telldus API.", sensor.Id, sensor.Name);
                        var content = await response.Content.ReadAsStringAsync();
                        var sensorData = System.Text.Json.JsonSerializer.Deserialize<SensorData>(content);
                        sensor.Data = sensorData?.Data ?? new List<Data>();
                    }
                    else
                    {
                        _logger.LogError("Failed to retrieve data for sensor {SensorId} with name {SensorName} from Telldus API. Status Code: {StatusCode}, Reason: {ReasonPhrase}", sensor.Id, sensor.Name, response.StatusCode, response.ReasonPhrase);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }
        await Task.WhenAll(tasks);
    }

    private async Task GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        bool isTokenValid = false;
        string usersettingsFilePath = Path.Combine(_appDataPath, "usersettings.json");
        if (File.Exists(usersettingsFilePath))
        {
            string jsonContent;
            try
            {
                jsonContent = await File.ReadAllTextAsync(usersettingsFilePath, cancellationToken);
                var settings = JsonSerializer.Deserialize<UserSettings>(jsonContent);
                if (settings != null && !string.IsNullOrEmpty(settings.AccessToken))
                {
                    _accessToken = settings.AccessToken;
                    var expires = settings.TokenExpiration;
                    if (expires.UtcDateTime < DateTimeOffset.UtcNow.AddMonths(-6))
                    {
                        _logger.LogWarning("Access token loaded from {UserSettingsFilePath} is older than 6 months. Attempting to refresh.", usersettingsFilePath);
                        try
                        {
                            var refreshResult = await GetRefreshTokenAsync(cancellationToken);
                            _accessToken = refreshResult.AccessToken;
                            expires = refreshResult.Expiration;
                            settings.AccessToken = _accessToken;
                            settings.TokenExpiration = expires;
                            string updatedJson = JsonSerializer.Serialize(settings);
                            await File.WriteAllTextAsync(usersettingsFilePath, updatedJson, cancellationToken);
                            _logger.LogInformation("Access token refreshed and saved to {UserSettingsFilePath}", usersettingsFilePath);
                            isTokenValid = true;
                        }
                        catch (Exception)
                        {
                            _logger.LogError("Failed to refresh access token. The existing token may be invalid or expired.");
                        }
                    }
                    if (await CheckAccessAsync(cancellationToken))
                    {
                        _logger.LogInformation("Access token loaded from {UserSettingsFilePath}", usersettingsFilePath);
                        isTokenValid = true;
                    }
                    else
                    {
                        _logger.LogWarning("Access token loaded from {UserSettingsFilePath} is invalid or expired.", usersettingsFilePath);
                    }
                    if (isTokenValid)
                    {
                        return;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialize user settings from {UserSettingsFilePath}. The file may be corrupted.", usersettingsFilePath);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to read user settings from {UserSettingsFilePath}. The file may be missing or locked.", usersettingsFilePath);
            }
        }
        var authResult = await FullAutomatedAuthAsync(cancellationToken);
        _accessToken = authResult.AccessToken;
        var offset = authResult.Expiration;
        if (!Directory.Exists(_appDataPath))
        {
            Directory.CreateDirectory(_appDataPath);
            _logger.LogInformation("Created application data directory at {AppDataPath}", _appDataPath);
        }
        var userSettings = new UserSettings { AccessToken = _accessToken, TokenExpiration = offset };
        string json = JsonSerializer.Serialize(userSettings);
        await File.WriteAllTextAsync(usersettingsFilePath, json);
        _logger.LogInformation("Access token saved to {UserSettingsFilePath}", usersettingsFilePath);
    }

    private async Task<bool> CheckAccessAsync(CancellationToken cancellationToken)
    {
        string endpoint = $"http://{_telldusSettings.Host}/api/system/info";

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

        // Pass the token in the Bearer authorization header
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Successfully connected to Telldus API.");
            var content = await response.Content.ReadAsStringAsync();
            return true;
        }
        else
        {
            _logger.LogError("Failed to connect to Telldus API. Status Code: {StatusCode}, Reason: {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);
            return false;
        }
    }

    private async Task<(string AccessToken, DateTimeOffset Expiration)> FullAutomatedAuthAsync(CancellationToken cancellationToken)
    {
        // Container to maintain cookies between login.telldus.com and the local TellStick
        var cookieContainer = new CookieContainer();
        using var handler = new HttpClientHandler { CookieContainer = cookieContainer, AllowAutoRedirect = true };
        using var client = new HttpClient(handler);

        // Step 1: Request temporary requestToken and authUrl
        string step1Url = $"http://{_telldusSettings.Host}/api/token";
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("app", _telldusSettings.AppName)
        });
        var step1HttpResponse = await client.PutAsync(step1Url, content, cancellationToken);
        if (!step1HttpResponse.IsSuccessStatusCode)
        {
            var errorBody = await step1HttpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Step 1 (request token) failed ({step1HttpResponse.StatusCode}): {errorBody}");
        }
        var step1Response = await step1HttpResponse.Content.ReadAsStringAsync(cancellationToken);

        using var step1Doc = JsonDocument.Parse(step1Response);
        string requestToken = step1Doc.RootElement.GetProperty("token").GetString()!;
        string authUrl = step1Doc.RootElement.GetProperty("authUrl").GetString()!;

        // Step 2: Visit authUrl to establish session, then perform web login
        var authPageResponse = await client.GetAsync(authUrl, cancellationToken);

        if (!authPageResponse.IsSuccessStatusCode)
        {
            var errorBody = await authPageResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Failed to retrieve auth page. Status: {authPageResponse.StatusCode}, Body: {errorBody}");
        }

        string loginUrl = authPageResponse.RequestMessage!.RequestUri!.ToString();

        var loginParams = new Dictionary<string, string>
        {
            { "email", _telldusSettings.Username },
            { "password", _telldusSettings.Password }
        };

        var loginContent = new FormUrlEncodedContent(loginParams);
        var loginResponse = await client.PostAsync(loginUrl, loginContent, cancellationToken);

        if (!loginResponse.IsSuccessStatusCode)
        {
            throw new Exception("Failed to authenticate with Telldus Live credentials.");
        }

        string approveUrl = authUrl;

        var approveParams = new Dictionary<string, string>
        {
            { "ttl", "525600" },
            { "extend", "1" },
            { "submit", "Authorize" }
        };

        var approveContent = new FormUrlEncodedContent(approveParams);

        var approveResponse = await client.PostAsync(approveUrl, approveContent, cancellationToken);

        if (!approveResponse.IsSuccessStatusCode)
        {
            var approveBody = await approveResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Failed to authorize token on the TellStick device. Status: {approveResponse.StatusCode}, Body: {approveBody}");
        }

        // Step 4: Exchange requestToken for the final Access Token
        // Small delay to allow the authorization to propagate to the TellStick device
        await Task.Delay(500, cancellationToken);

        string exchangeUrl = $"http://{_telldusSettings.Host}/api/token";
        var exchangeParams = new Dictionary<string, string?>
            {
                {"token", requestToken },
            };
        string fullUrl = QueryHelpers.AddQueryString(exchangeUrl, exchangeParams);

        var exchangeHttpResponse = await client.GetAsync(fullUrl, cancellationToken);
        if (!exchangeHttpResponse.IsSuccessStatusCode)
        {
            var errorBody = await exchangeHttpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Step 4 (exchange token) failed ({exchangeHttpResponse.StatusCode}): {errorBody}");
        }
        var exchangeResponse = await exchangeHttpResponse.Content.ReadAsStringAsync(cancellationToken);

        using var exchangeDoc = JsonDocument.Parse(exchangeResponse);
        var accessToken = exchangeDoc.RootElement.GetProperty("token").GetString()!;
        var expiresIn = exchangeDoc.RootElement.GetProperty("expires").GetInt32();
        var offset = DateTimeOffset.FromUnixTimeSeconds(expiresIn);
        return (AccessToken: accessToken, Expiration: offset);
    }

    private async Task<(string AccessToken, DateTimeOffset Expiration)> GetRefreshTokenAsync(CancellationToken cancellationToken)
    {
        string refreshUrl = $"http://{_telldusSettings.Host}/api/refreshToken";

        using var request = new HttpRequestMessage(HttpMethod.Get, refreshUrl);

        // Pass the token in the Bearer authorization header
        request.Headers.Add("Authorization", $"Bearer {_accessToken}");

        var refreshResponse = await _httpClient.SendAsync(request, cancellationToken);
        refreshResponse.EnsureSuccessStatusCode();

        var refreshContent = await refreshResponse.Content.ReadAsStringAsync();
        using var refreshDoc = JsonDocument.Parse(refreshContent);
        var accessToken = refreshDoc.RootElement.GetProperty("token").GetString();
        var expiresIn = refreshDoc.RootElement.GetProperty("expires").GetInt32();
        var offset = DateTimeOffset.FromUnixTimeSeconds(expiresIn);

        return (AccessToken: accessToken!, Expiration: offset);
    }
}
