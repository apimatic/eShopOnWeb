using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindbodyPublicApi;
using MindbodyPublicApi.Core.Configuration;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Owns the Mindbody SDK client and the staff user token. The token lives 24 hours, so it is cached until
/// shortly before it expires and re-issued on demand (or when Mindbody answers 401), which keeps the app
/// working for weeks without a restart.
/// </summary>
public class MindbodyConnection : IDisposable
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FallbackTokenLifetime = TimeSpan.FromHours(1);

    private readonly IOptionsMonitor<MindbodySettings> _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MindbodyConnection> _logger;
    private readonly object _clientLock = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private HttpClient? _httpClient;
    private MindbodyPublicApiClient? _client;
    private string? _clientFingerprint;

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public MindbodyConnection(IOptionsMonitor<MindbodySettings> settings, ILoggerFactory loggerFactory)
    {
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MindbodyConnection>();
    }

    public int NumericSiteId
    {
        get
        {
            var s = RequireSettings();
            return int.TryParse(s.SiteId, out var id)
                ? id
                : throw new StudioException(HttpStatusCode.ServiceUnavailable, "Mindbody:SiteId is not a number.");
        }
    }

    private MindbodySettings RequireSettings()
    {
        var s = _settings.CurrentValue;
        var missing = new[]
        {
            (nameof(s.ApiKey), s.ApiKey), (nameof(s.SiteId), s.SiteId),
            (nameof(s.Username), s.Username), (nameof(s.Password), s.Password)
        }.Where(p => string.IsNullOrWhiteSpace(p.Item2)).Select(p => $"{MindbodySettings.CONFIG_NAME}:{p.Item1}").ToList();
        if (missing.Count > 0)
        {
            throw new StudioException(HttpStatusCode.ServiceUnavailable,
                $"Studio bookings are not configured; missing setting(s): {string.Join(", ", missing)}.");
        }
        return s;
    }

    private MindbodyPublicApiClient GetClient()
    {
        var s = RequireSettings();
        var fingerprint = $"{s.ApiKey}|{s.BaseUrl}";
        lock (_clientLock)
        {
            if (_client != null && _clientFingerprint == fingerprint) return _client;

            // Recycle pooled connections regularly so DNS changes are honoured in a long-running process.
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            };
            var http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };

            var options = new MindbodyPublicApiClientOptions
            {
                ApiKey = s.ApiKey,
                // Keep the wait short: the mobile app gives up (and retries) quickly.
                Retry = RetryOptions.Default() with
                {
                    MaxRetries = 1,
                    Delay = TimeSpan.FromMilliseconds(300),
                    Timeout = TimeSpan.FromSeconds(12)
                },
                Logging = new LoggingOptions { LoggerFactory = _loggerFactory }
            };
            if (!string.IsNullOrWhiteSpace(s.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = s.BaseUrl!;
            }

            _httpClient?.Dispose();
            _httpClient = http;
            _clientFingerprint = fingerprint;
            _client = new MindbodyPublicApiClient(http, options);
            return _client;
        }
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, string? staleToken, CancellationToken ct)
    {
        if (!forceRefresh && _token != null && DateTimeOffset.UtcNow < _tokenExpiresAt - TokenRefreshMargin) return _token;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var fresh = _token != null && DateTimeOffset.UtcNow < _tokenExpiresAt - TokenRefreshMargin;
            // Another caller may already have replaced the token we found to be stale.
            if (fresh && (!forceRefresh || _token != staleToken)) return _token!;

            var s = RequireSettings();
            var response = await GetClient().UserToken.IssueToken(new IssueTokenRequest
            {
                SiteId = s.SiteId!,
                Body = new IssueRequest { Username = s.Username, Password = s.Password }
            }, cancellationToken: ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.AccessToken))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not issue an access token.");
            }
            _token = response.AccessToken;
            _tokenExpiresAt = response.Expires ?? DateTimeOffset.UtcNow + FallbackTokenLifetime;
            return _token!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// Runs a Mindbody call (client, site id, token) with a valid user token, refreshing the token once if
    /// Mindbody rejects it, and translating SDK failures into <see cref="StudioException"/>.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call, CancellationToken ct = default)
    {
        string? token = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                token = await GetTokenAsync(attempt > 0, token, ct).ConfigureAwait(false);
                return await call(GetClient(), RequireSettings().SiteId!, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogInformation("Mindbody rejected the cached token; issuing a new one.");
            }
            catch (StudioException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                throw Translate(ex);
            }
        }
    }

    /// <summary>
    /// For operations whose response body is not needed (cancellations): if Mindbody accepted the request
    /// but the SDK cannot read the body it sent back, the operation still succeeded.
    /// </summary>
    public async Task CallWriteAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call, CancellationToken ct = default)
    {
        try
        {
            await CallAsync(call, ct).ConfigureAwait(false);
        }
        catch (StudioException ex) when (ex.InnerException is ResponseDeserializationException { StatusCode: >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices })
        {
            _logger.LogWarning("Mindbody accepted the request but its response could not be read; treating it as done.");
        }
    }

    public static StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException api:
                var (code, message) = ReadError(api);
                var status = api.StatusCode switch
                {
                    HttpStatusCode.NotFound => HttpStatusCode.NotFound,
                    HttpStatusCode.Conflict => HttpStatusCode.Conflict,
                    HttpStatusCode.Unauthorized => HttpStatusCode.BadGateway,
                    (HttpStatusCode)429 => HttpStatusCode.ServiceUnavailable,
                    _ when (int)api.StatusCode >= 500 => HttpStatusCode.BadGateway,
                    _ => HttpStatusCode.BadRequest
                };
                if (status == HttpStatusCode.BadRequest && IsConflictCode(code)) status = HttpStatusCode.Conflict;
                return new StudioException(status, message, ex) { Code = code };
            case SdkTimeoutException:
            case TimeoutException:
            case TaskCanceledException:
                return new StudioException(HttpStatusCode.GatewayTimeout, "The studio system did not answer in time.", ex);
            case SdkException:
            case HttpRequestException:
                return new StudioException(HttpStatusCode.BadGateway, "The studio system could not be reached.", ex);
            default:
                return new StudioException(HttpStatusCode.BadGateway, "The studio system returned an unexpected response.", ex);
        }
    }

    private static bool IsConflictCode(string? code) =>
        code is "InvalidBookingTime" or "ValidationFailed" or "ClassFull" or "ClientAlreadyBooked";

    private static (string? Code, string Message) ReadError(ApiException api)
    {
        const string fallback = "The studio system rejected the request.";
        try
        {
            if (api is ApiException<RawError> raw)
            {
                using var doc = JsonDocument.Parse(raw.Error.ReadAsBytes());
                if (doc.RootElement.TryGetProperty("Error", out var err))
                {
                    var message = err.TryGetProperty("Message", out var m) ? m.GetString() : null;
                    var code = err.TryGetProperty("Code", out var c) ? c.GetString() : null;
                    return (code, string.IsNullOrWhiteSpace(message) ? fallback : message!);
                }
            }
        }
        catch (Exception)
        {
            // fall through to the generic reason
        }
        return (null, fallback);
    }

    public void Dispose() => _httpClient?.Dispose();
}
