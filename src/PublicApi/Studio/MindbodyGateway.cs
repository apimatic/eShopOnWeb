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
/// The single door to Mindbody: owns the SDK client and the staff user token, and turns every
/// failure of the upstream into a <see cref="StudioException"/> the API can report.
/// </summary>
/// <remarks>
/// The staff token Mindbody issues lives for about a day, while this app is meant to run for weeks,
/// so the token is re-issued shortly before it expires and again whenever Mindbody rejects it.
/// </remarks>
public sealed class MindbodyGateway : IDisposable
{
    private static readonly TimeSpan TokenRefreshMargin = TimeSpan.FromMinutes(10);
    // Used when Mindbody does not tell us when a token expires.
    private static readonly TimeSpan AssumedTokenLifetime = TimeSpan.FromHours(1);

    private readonly MindbodySettings _settings;
    private readonly TimeProvider _time;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MindbodyGateway> _logger;
    private readonly object _clientGate = new();
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private HttpClient? _http;
    private MindbodyPublicApiClient? _client;
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public MindbodyGateway(IOptions<MindbodySettings> settings, TimeProvider time, ILoggerFactory loggerFactory)
    {
        _settings = settings.Value;
        _time = time;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MindbodyGateway>();
    }

    public string SiteId => RequireSetting(_settings.SiteId, "Mindbody:SiteId");

    public TimeProvider Time => _time;

    /// <summary>Runs an upstream call with a valid staff token, re-authenticating once if the token is rejected.</summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, Task<T>> call, CancellationToken cancellationToken)
    {
        var client = GetClient();
        var attemptedRefresh = false;
        while (true)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await call(client, token).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && !attemptedRefresh)
            {
                attemptedRefresh = true;
                await InvalidateAsync(token, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                throw Translate(ex);
            }
        }
    }

    private async Task InvalidateAsync(string rejectedToken, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_token, rejectedToken, StringComparison.Ordinal))
            {
                _token = null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetCachedToken(out var cached))
        {
            return cached;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCachedToken(out cached))
            {
                return cached;
            }

            var client = GetClient();
            IssueResponse response;
            try
            {
                response = await client.UserToken.IssueToken(new IssueTokenRequest
                {
                    SiteId = SiteId,
                    Body = new IssueRequest
                    {
                        Username = RequireSetting(_settings.Username, "Mindbody:Username"),
                        Password = RequireSetting(_settings.Password, "Mindbody:Password"),
                    },
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not StudioException && ex is not OperationCanceledException)
            {
                // Never surface what Mindbody said about our credentials to a shopper.
                _logger.LogError(ex, "Could not obtain a Mindbody staff token");
                throw new StudioException(502, "The studio system could not be reached right now. Please try again later.", ex);
            }

            if (string.IsNullOrEmpty(response.AccessToken))
            {
                throw new StudioException(502, "The studio system did not grant access. Please try again later.");
            }

            _token = response.AccessToken;
            _tokenExpiresAt = response.Expires ?? _time.GetUtcNow() + AssumedTokenLifetime;
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool TryGetCachedToken(out string token)
    {
        var current = _token;
        if (current is not null && _time.GetUtcNow() < _tokenExpiresAt - TokenRefreshMargin)
        {
            token = current;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private MindbodyPublicApiClient GetClient()
    {
        if (_client is not null)
        {
            return _client;
        }

        lock (_clientGate)
        {
            if (_client is not null)
            {
                return _client;
            }

            var options = new MindbodyPublicApiClientOptions
            {
                ApiKey = RequireSetting(_settings.ApiKey, "Mindbody:ApiKey"),
                TimeProvider = _time,
                // Keep upstream waits short: the mobile app gives up and asks again quickly.
                // Only idempotent calls are retried by the SDK; bookings are protected by this app instead.
                Retry = RetryOptions.Default() with
                {
                    Timeout = TimeSpan.FromSeconds(12),
                    MaxRetries = 2,
                    Delay = TimeSpan.FromMilliseconds(400),
                },
                Logging = new LoggingOptions { LoggerFactory = _loggerFactory },
            };

            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
            }

            // A weeks-long process must not pin one connection (and one DNS answer) forever.
            _http = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            {
                Timeout = Timeout.InfiniteTimeSpan, // the SDK enforces its own per-attempt timeout
            };

            _client = new MindbodyPublicApiClient(_http, options);
            return _client;
        }
    }

    private static string RequireSetting(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new StudioException(503, $"The studio integration is not configured ({name} is missing).");
        }

        return value;
    }

    private StudioException Translate(Exception ex)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogWarning(ex, "Mindbody call timed out");
                return new StudioException(504, "The studio system took too long to answer. Please try again.", ex);

            case ApiException api:
                var message = ExtractMessage(api);
                var status = (int)api.StatusCode;
                _logger.LogWarning(ex, "Mindbody answered {Status}: {Message}", status, message);
                return status switch
                {
                    404 => new StudioException(404, message ?? "The studio system could not find what was asked for.", ex),
                    429 => new StudioException(503, "The studio system is busy. Please try again shortly.", ex),
                    >= 400 and < 500 and not 401 and not 403 =>
                        new StudioException(409, message ?? "The studio system rejected the request.", ex),
                    _ => new StudioException(502, "The studio system failed to process the request. Please try again later.", ex),
                };

            case SdkException or HttpRequestException or TaskCanceledException or JsonException:
                _logger.LogWarning(ex, "Mindbody call failed");
                return new StudioException(502, "The studio system could not be reached right now. Please try again later.", ex);

            default:
                _logger.LogError(ex, "Unexpected failure calling Mindbody");
                return new StudioException(502, "The studio system failed to process the request. Please try again later.", ex);
        }
    }

    private static string? ExtractMessage(ApiException ex)
    {
        if (ex is ApiException<RawError> raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw.Error.ReadAsBytes());
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Error", out var error) &&
                    error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("Message", out var message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        return null;
    }

    public void Dispose()
    {
        _http?.Dispose();
        _tokenLock.Dispose();
    }
}
