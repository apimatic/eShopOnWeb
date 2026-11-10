using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindbodyPublicApi;
using MindbodyPublicApi.Core;
using MindbodyPublicApi.Core.Configuration;
using MindbodyPublicApi.Core.Models;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.SiteApi;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Single point of contact with Mindbody (through the SDK): owns the SDK client, keeps the staff user token fresh,
/// knows the site's time zone and turns SDK failures into <see cref="StudioException"/>s.
/// Registered as a singleton; everything it caches is refreshed on expiry because the app runs for weeks.
/// </summary>
public class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan SiteInfoLifetime = TimeSpan.FromHours(6);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly TimeProvider _clock;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly SemaphoreSlim _siteLock = new(1, 1);
    private readonly Lazy<MindbodyPublicApiClient> _client;

    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
    private TimeZoneInfo? _siteTimeZone;
    private DateTimeOffset _siteInfoExpiry = DateTimeOffset.MinValue;

    public MindbodyGateway(IOptions<MindbodySettings> settings, ILogger<MindbodyGateway> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _clock = TimeProvider.System;

        // A long-lived app must not pin DNS answers forever: recycle pooled connections regularly.
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _client = new Lazy<MindbodyPublicApiClient>(CreateClient, LazyThreadSafetyMode.PublicationOnly);
    }

    public string SiteId => Required(_settings.SiteId, "Mindbody:SiteId");

    private MindbodyPublicApiClient CreateClient()
    {
        var options = new MindbodyPublicApiClientOptions
        {
            ApiKey = Required(_settings.ApiKey, "Mindbody:ApiKey"),
            TimeProvider = _clock,
            // Answer the mobile app promptly: bounded attempts and a small number of retries for reads.
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(15), MaxRetries = 2, Delay = TimeSpan.FromMilliseconds(500) },
        };
        if (!string.IsNullOrEmpty(_settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
        }
        return new MindbodyPublicApiClient(_httpClient, options);
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new StudioException(HttpStatusCode.ServiceUnavailable, $"The studio integration is not configured ({key} is missing).")
            : value;

    /// <summary>
    /// Runs <paramref name="call"/> with a valid staff token. A rejected token is replaced once and the call repeated.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, Task<T>> call, CancellationToken cancellationToken = default)
    {
        var client = _client.Value;

        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            try
            {
                return await call(client, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (attempt == 0 && ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Mindbody rejected the staff token; issuing a new one.");
                await GetTokenAsync(forceRefresh: true, cancellationToken, rejectedToken: token).ConfigureAwait(false);
            }
            catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and <= 299)
            {
                _logger.LogWarning(ex, "Mindbody call succeeded but its reply could not be read");
                throw new UnreadableReplyException(ex);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Mindbody call failed");
                throw Translate(ex);
            }
        }
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken, string? rejectedToken = null)
    {
        if (!forceRefresh && _token is { } cached && _clock.GetUtcNow() < _tokenExpiry - TokenSafetyMargin)
        {
            return cached;
        }

        await _authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.GetUtcNow();
            if (_token is not null && now < _tokenExpiry - TokenSafetyMargin && _token != rejectedToken)
            {
                return _token;
            }

            try
            {
                var response = await _client.Value.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = SiteId,
                    Body = new MindbodyPublicApi.Models.IssueRequest
                    {
                        Username = Required(_settings.Username, "Mindbody:Username"),
                        Password = Required(_settings.Password, "Mindbody:Password"),
                    },
                }, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(response.AccessToken))
                {
                    throw new StudioException(HttpStatusCode.BadGateway, "Mindbody did not issue a staff token.");
                }

                _token = response.AccessToken;
                _tokenExpiry = response.Expires ?? now + DefaultTokenLifetime;
                return _token;
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                _token = null;
                _tokenExpiry = DateTimeOffset.MinValue;
                throw Translate(ex);
            }
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>The site's time zone (class and appointment times in Mindbody are wall-clock times in this zone).</summary>
    public async Task<TimeZoneInfo> GetSiteTimeZoneAsync(CancellationToken cancellationToken = default)
    {
        if (_siteTimeZone is { } cached && _clock.GetUtcNow() < _siteInfoExpiry)
        {
            return cached;
        }

        await _siteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_siteTimeZone is { } again && _clock.GetUtcNow() < _siteInfoExpiry)
            {
                return again;
            }

            var siteId = SiteId;
            GetSitesResponse response;
            try
            {
                // The SDK's sites operation has no SiteId parameter, so the site header is added per request.
                var options = new RequestOptions { Headers = [new HeaderParam("siteId", siteId)] };
                response = await CallAsync((client, token) => client.SiteApi.GetSites(new GetSitesOperationRequest
                {
                    Authorization = token,
                    RequestSiteIds = int.TryParse(siteId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? [id] : null,
                }, options, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }

            var zoneId = response.Sites is { Count: > 0 } sites ? sites[0].TimeZone : null;
            if (string.IsNullOrEmpty(zoneId))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "Mindbody did not report the studio's time zone.");
            }

            try
            {
                _siteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new StudioException(HttpStatusCode.BadGateway, $"The studio's time zone '{zoneId}' is not known on this server.", ex);
            }

            _siteInfoExpiry = _clock.GetUtcNow() + SiteInfoLifetime;
            return _siteTimeZone;
        }
        finally
        {
            _siteLock.Release();
        }
    }

    public void DebugLog(string m) => _logger.LogWarning("{m}", m);
    public DateTimeOffset UtcNow => _clock.GetUtcNow();

    /// <summary>Turns SDK / transport failures into the status the endpoint should answer with.</summary>
    public static StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case StudioException studio:
                return studio;
            case ApiException api:
            {
                var message = ReadMindbodyMessage(api);
                var status = (int)api.StatusCode;
                if (status == 404)
                {
                    return new StudioException(HttpStatusCode.NotFound, message, ex);
                }
                if (status == 409)
                {
                    return new StudioException(HttpStatusCode.Conflict, message, ex);
                }
                if (status == 429)
                {
                    return new StudioException(HttpStatusCode.ServiceUnavailable, "The studio system is busy; please try again shortly.", ex);
                }
                if (status >= 400 && status < 500 && status != 401 && status != 403 && status != 408)
                {
                    return new StudioException(HttpStatusCode.BadRequest, message, ex);
                }
                return new StudioException(HttpStatusCode.BadGateway, $"The studio system could not process the request: {message}", ex);
            }
            case SdkTimeoutException:
                return new StudioException(HttpStatusCode.GatewayTimeout, "The studio system did not answer in time.", ex);
            case SdkException or HttpRequestException or TimeoutException:
                return new StudioException(HttpStatusCode.BadGateway, "The studio system is unavailable.", ex);
            default:
                return new StudioException(HttpStatusCode.BadGateway, "The studio system returned an unexpected response.", ex);
        }
    }

    private static string ReadMindbodyMessage(ApiException api)
    {
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            if (error is RawError raw)
            {
                using var doc = JsonDocument.Parse(raw.ReadAsString());
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Error", out var e) &&
                    e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    return m.GetString()!;
                }
            }
        }
        catch (Exception)
        {
            // fall through to the generic message
        }
        return $"The studio system rejected the request ({(int)api.StatusCode}).";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _authLock.Dispose();
        _siteLock.Dispose();
    }
}
