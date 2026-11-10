using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MindbodyPublicApi;
using MindbodyPublicApi.Core.Configuration;
using MindbodyPublicApi.Core.ErrorResponse;
using MindbodyPublicApi.Core.Exceptions;
using MindbodyPublicApi.Models;
using MindbodyPublicApi.Requests.UserToken;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Owns the Mindbody SDK client and the staff user token used to call it. The token is issued on demand,
/// renewed shortly before it expires and re-issued when Mindbody rejects it, so the application keeps
/// working for as long as it runs. Registered as a singleton.
/// </summary>
public sealed class MindbodyConnection : IDisposable
{
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromMinutes(10);

    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyConnection> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly object _clientLock = new();
    private MindbodyPublicApiClient? _client;
    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public MindbodyConnection(MindbodySettings settings, ILogger<MindbodyConnection> logger)
    {
        _settings = settings;
        _logger = logger;
        // Connections are recycled regularly so DNS changes are picked up by a long running process.
        _httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public string SiteId => Require(_settings.SiteId, "Mindbody:SiteId");

    private static string Require(string? value, string key) =>
        value ?? throw new StudioException(HttpStatusCode.ServiceUnavailable,
            $"The studio integration is not configured ({key} is missing).");

    private MindbodyPublicApiClient Client()
    {
        if (_client != null)
        {
            return _client;
        }

        lock (_clientLock)
        {
            if (_client != null)
            {
                return _client;
            }

            var options = new MindbodyPublicApiClientOptions
            {
                ApiKey = Require(_settings.ApiKey, "Mindbody:ApiKey"),
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20), MaxRetries = 2 },
            };
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
            }

            _client = new MindbodyPublicApiClient(_httpClient, options);
            return _client;
        }
    }

    /// <summary>
    /// Runs a Mindbody call with a valid staff token. When Mindbody rejects the token, a new one is issued and
    /// the call is repeated once. Failures are translated into <see cref="StudioException"/>.
    /// </summary>
    public async Task<T> CallAsync<T>(Func<MindbodyPublicApiClient, string, string, Task<T>> call,
        CancellationToken cancellationToken = default)
    {
        var client = Client();
        var siteId = SiteId;
        try
        {
            var token = await GetTokenAsync(null, cancellationToken);
            try
            {
                return await call(client, siteId, token);
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogInformation("Mindbody rejected the staff token, issuing a new one.");
                token = await GetTokenAsync(token, cancellationToken);
                return await call(client, siteId, token);
            }
        }
        catch (StudioException)
        {
            throw;
        }
        catch (ApiException ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogWarning(ex, "Mindbody timed out.");
            throw new StudioException(HttpStatusCode.GatewayTimeout, "The studio system did not answer in time. Please try again.");
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Mindbody call failed.");
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system could not be reached. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Mindbody call failed.");
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system could not be reached. Please try again.");
        }
    }

    private async Task<string> GetTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        if (rejectedToken == null && _token != null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _token;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed the token while this one was waiting.
            if (_token != null && _token != rejectedToken && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _token;
            }

            var response = await Client().UserToken.IssueToken(new IssueTokenRequest
            {
                SiteId = SiteId,
                Body = new IssueRequest
                {
                    Username = Require(_settings.Username, "Mindbody:Username"),
                    Password = Require(_settings.Password, "Mindbody:Password"),
                }
            }, cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(response.AccessToken))
            {
                throw new StudioException(HttpStatusCode.BadGateway, "The studio system did not issue an access token.");
            }

            _token = response.AccessToken;
            var expiry = response.Expires ?? DateTimeOffset.UtcNow.AddHours(1);
            _tokenExpiry = expiry - TokenSafetyMargin;
            return _token;
        }
        catch (ApiException ex)
        {
            _logger.LogError("Mindbody refused to issue a staff token: {Status}", ex.StatusCode);
            throw new StudioException(HttpStatusCode.BadGateway, "The studio system refused the integration's credentials.");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private StudioException Translate(ApiException ex)
    {
        var message = ReadMessage(ex);
        _logger.LogWarning(ex, "Mindbody answered {Status}: {Message}", (int)ex.StatusCode, message);

        if (ex.StatusCode == HttpStatusCode.TooManyRequests || (int)ex.StatusCode >= 500)
        {
            return new StudioException(HttpStatusCode.ServiceUnavailable,
                "The studio system is busy or unavailable. Please try again.");
        }

        if (ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return new StudioException(HttpStatusCode.BadGateway, "The studio system refused the integration's credentials.");
        }

        var status = ex.StatusCode == HttpStatusCode.NotFound ? HttpStatusCode.NotFound : HttpStatusCode.UnprocessableEntity;
        return new StudioException(status, message ?? "The studio system could not carry out the request.");
    }

    private static string? ReadMessage(ApiException ex)
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
                // Not a Mindbody error document; fall through.
            }
        }
        return null;
    }

    /// <summary>Reads every page of a paged Mindbody listing.</summary>
    public static async Task<List<T>> PageAllAsync<T>(Func<int, int, Task<(IReadOnlyList<T>? Items, int? Total)>> page,
        int pageSize = 200)
    {
        var all = new List<T>();
        var offset = 0;
        while (true)
        {
            var (items, total) = await page(pageSize, offset);
            var count = items?.Count ?? 0;
            if (items != null)
            {
                all.AddRange(items);
            }
            offset += count;
            if (count == 0 || (total.HasValue && offset >= total.Value) || (!total.HasValue && count < pageSize))
            {
                break;
            }
        }
        return all;
    }

    public void Dispose() => _httpClient.Dispose();
}
