using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Advanced Billing REST API.
/// <para>
/// Hand-written rather than generated: the API's replay protection (<c>uniqueness_token</c>) is a
/// sibling of the resource object in the request body, which the published SDK models cannot express,
/// and it is the mechanism this integration relies on for idempotent subscribe.
/// </para>
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio authenticates with the API key as the Basic user name and "x" as the password.</summary>
    private const string BasicAuthPassword = "x";

    /// <summary>Maxio caps a page at 200 records.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guards against an unbounded loop if the service ever stops honouring paging.</summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Property names are pinned with [JsonPropertyName]; omitting nulls keeps optional
        // attributes out of the request rather than sending them as explicit nulls.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioSettings> settings, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSiteEnvelope>(HttpMethod.Get, "site.json", body: null, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Site ?? throw new MaxioApiException(HttpStatusCode.OK, "GET", "site.json",
            new[] { "Response contained no site object." });
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        // A product family may be addressed by numeric id or by "handle:<handle>". Handles are stable
        // across catalog re-seeds; ids are not, so the handle form is the only safe one to configure.
        var familySegment = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={MaxPageSize}";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
                .ConfigureAwait(false);

            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            foreach (var envelope in envelopes)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, body: null, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Maxio reports "no customer with this reference" as a 404 with an empty body.
            return null;
        }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Customer ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json",
            new[] { "Response contained no customer object." });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(HttpMethod.Get, path, body: null, cancellationToken)
            .ConfigureAwait(false);

        var subscriptions = new List<MaxioSubscription>();
        foreach (var envelope in envelopes ?? new List<MaxioSubscriptionEnvelope>())
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";

        try
        {
            var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Get, path, body: null, cancellationToken)
                .ConfigureAwait(false);
            return envelope?.Subscription;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Subscription ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json",
            new[] { "Response contained no subscription object." });
    }

    /// <summary>
    /// Issues one logical call, retrying transient failures. The request message is rebuilt for every
    /// attempt because an <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    /// <remarks>
    /// Retrying a POST is safe because every record this client creates carries an application-chosen
    /// <c>reference</c>, which Maxio enforces as unique per site: if the first attempt reached Maxio
    /// but its response was lost, the retry is refused with 422 instead of creating a second record,
    /// and the caller reads the original back.
    /// </remarks>
    private async Task<TResponse?> SendAsync<TResponse>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var attempts = _settings.MaxRetries + 1;

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= attempts;

            try
            {
                using var request = BuildRequest(method, path, body);
                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return await ReadContentAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
                }

                if (!isLastAttempt && IsRetryableStatus(response.StatusCode))
                {
                    var delay = GetRetryDelay(response, attempt);
                    _logger.LogWarning(
                        "Maxio {Method} {Path} returned {StatusCode}; retrying in {DelayMs}ms (attempt {Attempt} of {Attempts}).",
                        method.Method, PathForLog(path), (int)response.StatusCode, delay.TotalMilliseconds, attempt, attempts);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw await BuildApiExceptionAsync(response, method, path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // TaskCanceledException without a caller cancellation means the per-attempt timeout fired.
                if (isLastAttempt)
                {
                    throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, method.Method, PathForLog(path),
                        new[] { $"Could not reach Maxio after {attempts} attempt(s): {ex.Message}" });
                }

                var delay = GetBackoffDelay(attempt);
                _logger.LogWarning(ex,
                    "Maxio {Method} {Path} failed to complete; retrying in {DelayMs}ms (attempt {Attempt} of {Attempts}).",
                    method.Method, PathForLog(path), delay.TotalMilliseconds, attempt, attempts);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Set per request rather than on the shared HttpClient so a rotated key takes effect without
        // waiting for the pooled handler to be recycled.
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:{BasicAuthPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, body.GetType(), SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task<TResponse?> ReadContentAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TResponse>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<MaxioApiException> BuildApiExceptionAsync(HttpResponseMessage response, HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var errors = await ReadErrorsAsync(response, cancellationToken).ConfigureAwait(false);
        return new MaxioApiException(response.StatusCode, method.Method, PathForLog(path), errors);
    }

    private static async Task<IReadOnlyList<string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string payload;
        try
        {
            payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Array.Empty<string>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<MaxioErrorResponse>(payload, SerializerOptions);
            if (parsed?.Errors is { Count: > 0 })
            {
                return parsed.Errors;
            }
        }
        catch (JsonException)
        {
            // Not the documented error envelope - fall through and surface the raw body instead.
        }

        return new[] { Truncate(payload, 500) };
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false
    };

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
            {
                return until;
            }
        }

        return GetBackoffDelay(attempt);
    }

    /// <summary>Exponential backoff with jitter, so retries from parallel callers do not synchronise.</summary>
    private static TimeSpan GetBackoffDelay(int attempt)
    {
        var baseDelayMs = 200d * Math.Pow(2, attempt - 1);
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(Math.Min(baseDelayMs + jitterMs, 10_000));
    }

    /// <summary>Strips the query string so lookup values never reach the logs.</summary>
    private static string PathForLog(string path)
    {
        var queryStart = path.IndexOf('?');
        return queryStart < 0 ? path : path[..queryStart];
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
