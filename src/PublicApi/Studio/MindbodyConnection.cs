using System;
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
/// The one connection to Mindbody: owns the SDK client and the staff user token, and turns every
/// failure into a <see cref="StudioException"/> a caller can be told about.
/// </summary>
public sealed class MindbodyConnection : IDisposable
{
    // A staff token lives 24 hours. It is replaced well before then, so that a process which runs for
    // weeks never sends an expired one.
    private static readonly TimeSpan TokenRenewMargin = TimeSpan.FromMinutes(30);

    private readonly MindbodySettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<MindbodyConnection> _logger;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly HttpClient _http;
    private readonly Lazy<MindbodyPublicApiClient> _client;
    private (string Token, DateTimeOffset Expires)? _token;

    public MindbodyConnection(IOptions<MindbodySettings> settings, TimeProvider clock, ILogger<MindbodyConnection> logger)
    {
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;

        // Connections are recycled regularly so that a long-running process follows DNS changes.
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        _client = new Lazy<MindbodyPublicApiClient>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private MindbodyPublicApiClient CreateClient()
    {
        var options = new MindbodyPublicApiClientOptions
        {
            ApiKey = _settings.ApiKey,
            TimeProvider = _clock,
            // The shopper's app gives up after a while and asks again, so one call must not take minutes.
            Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = TimeSpan.FromSeconds(20) },
        };
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
        }

        return new MindbodyPublicApiClient(_http, options);
    }

    public string SiteId => _settings.SiteId!;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SiteId)
            || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new StudioException(HttpStatusCode.ServiceUnavailable,
                "Studio bookings are not available: the Mindbody settings are not configured.");
        }
    }

    /// <summary>Runs a call against Mindbody with a valid staff token and the site id.</summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call, CancellationToken ct)
    {
        EnsureConfigured();
        var attemptedRefresh = false;
        while (true)
        {
            var token = await GetTokenAsync(attemptedRefresh, ct).ConfigureAwait(false);
            try
            {
                return await call(_client.Value, SiteId, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (!attemptedRefresh
                && (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden))
            {
                // The token may have been revoked or expired early: get a new one once.
                attemptedRefresh = true;
                _logger.LogInformation("Mindbody rejected the staff token ({Status}); requesting a new one.", (int)ex.StatusCode);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
    }

    private async Task<string> GetTokenAsync(bool forceRenew, CancellationToken ct)
    {
        if (!forceRenew && _token is { } t && t.Expires - _clock.GetUtcNow() > TokenRenewMargin)
        {
            return t.Token;
        }

        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRenew && _token is { } again && again.Expires - _clock.GetUtcNow() > TokenRenewMargin)
            {
                return again.Token;
            }

            try
            {
                var response = await _client.Value.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = SiteId,
                    Body = new IssueRequest { Username = _settings.Username, Password = _settings.Password },
                }, cancellationToken: ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(response.AccessToken))
                {
                    throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not issue an access token.");
                }

                // Never trust a missing expiry: assume the documented 24 hours.
                var expires = response.Expires ?? _clock.GetUtcNow().AddHours(24);
                _token = (response.AccessToken!, expires);
                return response.AccessToken!;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest
                || ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _token = null;
                _logger.LogError("Mindbody refused the configured staff credentials ({Status}).", (int)ex.StatusCode);
                throw new StudioException(HttpStatusCode.BadGateway,
                    "The studio system refused the app's credentials. Please try again later.", ex);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    /// <summary>Turns an SDK failure into the exception the API answers with.</summary>
    public StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case StudioException s:
                return s;
            case ResponseDeserializationException unreadable when (int)unreadable.StatusCode is >= 200 and < 300:
                _logger.LogWarning(unreadable, "Mindbody answered {Status} but its reply could not be read.", (int)unreadable.StatusCode);
                return new StudioException(HttpStatusCode.BadGateway,
                    "The studio system answered, but its answer could not be understood.", ex) { ReplyUnreadable = true };
            case ApiException api:
                var (code, message) = ReadError(api);
                var status = (int)api.StatusCode;
                if (status == 429 || status >= 500)
                {
                    _logger.LogWarning("Mindbody answered {Status}: {Message}", status, message);
                    return new StudioException(HttpStatusCode.ServiceUnavailable,
                        "The studio system is busy or unavailable. Please try again shortly.", ex)
                    { RetryAfterSeconds = 5, MindbodyCode = code };
                }

                if (status is 401 or 403)
                {
                    _logger.LogError("Mindbody refused the app's access ({Status}): {Message}", status, message);
                    return new StudioException(HttpStatusCode.BadGateway,
                        "The studio system refused the app's access. Please try again later.", ex) { MindbodyCode = code };
                }

                if (status == 404)
                {
                    return new StudioException(HttpStatusCode.NotFound, message, ex) { MindbodyCode = code };
                }

                return new StudioException(HttpStatusCode.UnprocessableEntity, message, ex) { MindbodyCode = code };
            case SdkTimeoutException:
                return new StudioException(HttpStatusCode.GatewayTimeout,
                    "The studio system took too long to answer. Please try again.", ex) { RetryAfterSeconds = 2 };
            case SdkConnectionException:
            case HttpRequestException:
                return new StudioException(HttpStatusCode.BadGateway,
                    "The studio system could not be reached. Please try again.", ex) { RetryAfterSeconds = 2 };
            case TaskCanceledException:
                return new StudioException(HttpStatusCode.GatewayTimeout,
                    "The studio system took too long to answer. Please try again.", ex) { RetryAfterSeconds = 2 };
            default:
                _logger.LogError(ex, "Unexpected failure talking to Mindbody.");
                return new StudioException(HttpStatusCode.BadGateway, "The studio system failed to handle the request.", ex);
        }
    }

    private static (string? Code, string Message) ReadError(ApiException api)
    {
        if (api is ApiException<RawError> raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw.Error.ReadAsBytes());
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    var message = error.TryGetProperty("Message", out var m) ? m.GetString() : null;
                    var code = error.TryGetProperty("Code", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return (code, message!);
                    }
                }
            }
            catch (JsonException)
            {
                // fall through to the generic message
            }
        }

        return (null, $"The studio system rejected the request (HTTP {(int)api.StatusCode}).");
    }

    public void Dispose()
    {
        _http.Dispose();
        _tokenGate.Dispose();
    }
}
