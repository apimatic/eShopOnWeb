using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
/// </summary>
/// <remarks>
/// Owns transport concerns only: authentication, serialization, concurrency, retries, and the
/// translation of Maxio error payloads into <see cref="MaxioApiException"/>. Enrollment policy
/// lives in <see cref="MaxioSubscriptionService"/>.
/// </remarks>
public class MaxioApiClient : IMaxioApiClient
{
    /// <summary>Maxio caps a page at 200 items; ask for the maximum to keep round-trips down.</summary>
    private const int MaxPageSize = 200;

    /// <summary>Guard against an unbounded loop if a site ever stops honouring pagination.</summary>
    private const int MaxPages = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly MaxioRequestGate _gate;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(
        HttpClient httpClient,
        MaxioRequestGate gate,
        IOptions<MaxioSettings> settings,
        ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _gate = gate;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", allowNotFound: false, cancellationToken)
            .ConfigureAwait(false);
        return envelope?.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        // Maxio addresses a family either by numeric id or by "handle:<handle>". The handle form is
        // the stable one: ids are reassigned whenever a catalog is re-seeded.
        var familySegment = "handle:" + Uri.EscapeDataString(productFamilyHandle.Trim());
        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var path = $"product_families/{familySegment}/products.json?page={page}&per_page={MaxPageSize}";
            var envelopes = await GetAsync<List<MaxioProductEnvelope>>(path, allowNotFound: false, cancellationToken)
                .ConfigureAwait(false);

            if (envelopes is null || envelopes.Count == 0)
            {
                break;
            }

            products.AddRange(envelopes.Select(e => e.Product).OfType<MaxioProduct>());

            if (envelopes.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string reference,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(reference));
        }

        var path = "customers/lookup.json?reference=" + Uri.EscapeDataString(reference);

        // Maxio answers 404 when no customer carries the reference. That is the "not enrolled yet"
        // answer, not a failure.
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, allowNotFound: true, cancellationToken)
            .ConfigureAwait(false);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        CreateCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        const string path = "customers.json";

        var envelope = await PostAsync<CreateCustomerRequest, MaxioCustomerEnvelope>(
            path,
            request,
            safeToRetry: !string.IsNullOrEmpty(request.UniquenessToken),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Customer ?? throw MissingPayload(HttpMethod.Post.Method, path, "customer");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(path, allowNotFound: true, cancellationToken)
            .ConfigureAwait(false);

        return envelopes is null
            ? Array.Empty<MaxioSubscription>()
            : envelopes.Select(e => e.Subscription).OfType<MaxioSubscription>().ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        CreateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        const string path = "subscriptions.json";

        var envelope = await PostAsync<CreateSubscriptionRequest, MaxioSubscriptionEnvelope>(
            path,
            request,
            // Replaying an enrollment is only safe because the token makes Maxio reject the
            // duplicate instead of enrolling the shopper twice.
            safeToRetry: !string.IsNullOrEmpty(request.UniquenessToken),
            cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription ?? throw MissingPayload(HttpMethod.Post.Method, path, "subscription");
    }

    private static MaxioApiException MissingPayload(string method, string path, string expected) =>
        new(
            HttpStatusCode.OK,
            method,
            path,
            new[] { $"Maxio returned a success status with no {expected} payload." });

    private Task<TResponse?> GetAsync<TResponse>(string path, bool allowNotFound, CancellationToken cancellationToken)
        where TResponse : class =>
        SendAsync<TResponse>(
            () => new HttpRequestMessage(HttpMethod.Get, path),
            HttpMethod.Get.Method,
            path,
            allowNotFound,
            safeToRetry: true,
            cancellationToken);

    private Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest body,
        bool safeToRetry,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        // Serialize once: the payload is re-sent verbatim on every attempt, and HttpContent cannot
        // be reused across HttpRequestMessage instances.
        var json = JsonSerializer.Serialize(body, SerializerOptions);

        return SendAsync<TResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            HttpMethod.Post.Method,
            path,
            allowNotFound: false,
            safeToRetry,
            cancellationToken);
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        Func<HttpRequestMessage> requestFactory,
        string method,
        string path,
        bool allowNotFound,
        bool safeToRetry,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var maxAttempts = safeToRetry ? Math.Max(1, _settings.MaxAttempts) : 1;

        for (var attempt = 1; ; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage? response = null;
            Exception? transportFailure = null;

            try
            {
                using var request = requestFactory();
                using (await _gate.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    response = await _httpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
            {
                transportFailure = ex;
            }

            stopwatch.Stop();

            try
            {
                if (response is not null)
                {
                    _logger.LogDebug(
                        "Maxio {Method} {Path} responded {StatusCode} in {ElapsedMs}ms (attempt {Attempt} of {MaxAttempts}).",
                        method, Redact(path), (int)response.StatusCode, stopwatch.ElapsedMilliseconds, attempt, maxAttempts);

                    if (response.IsSuccessStatusCode)
                    {
                        return await ReadPayloadAsync<TResponse>(response, method, path, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return null;
                    }

                    if (attempt < maxAttempts && IsRetryableStatus(response.StatusCode))
                    {
                        var statusDelay = GetRetryDelay(attempt, response);
                        _logger.LogWarning(
                            "Maxio {Method} {Path} returned {StatusCode}; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                            method, Redact(path), (int)response.StatusCode, statusDelay.TotalMilliseconds, attempt, maxAttempts);

                        await Task.Delay(statusDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw await BuildApiExceptionAsync(response, method, path, cancellationToken).ConfigureAwait(false);
                }

                if (attempt < maxAttempts)
                {
                    var transportDelay = GetRetryDelay(attempt, response: null);
                    _logger.LogWarning(
                        transportFailure,
                        "Maxio {Method} {Path} could not be completed; retrying in {DelayMs}ms (attempt {Attempt} of {MaxAttempts}).",
                        method, Redact(path), transportDelay.TotalMilliseconds, attempt, maxAttempts);

                    await Task.Delay(transportDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new MaxioApiException(
                    HttpStatusCode.ServiceUnavailable,
                    method,
                    path,
                    new[] { transportFailure?.Message ?? "The request to Maxio could not be completed." },
                    transportFailure);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private static async Task<TResponse?> ReadPayloadAsync<TResponse>(
        HttpResponseMessage response,
        string method,
        string path,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(content, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(
                response.StatusCode,
                method,
                path,
                new[] { "Maxio returned a payload that could not be parsed: " + ex.Message },
                ex);
        }
    }

    private static async Task<MaxioApiException> BuildApiExceptionAsync(
        HttpResponseMessage response,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new MaxioApiException(response.StatusCode, method, path, ParseErrors(body));
    }

    /// <summary>
    /// Maxio reports failures as <c>{"errors": ["..."]}</c>, and on some endpoints as a map of
    /// field to messages. Read both shapes, and fall back to the raw body so that an unrecognised
    /// payload is surfaced rather than silently swallowed.
    /// </summary>
    internal static IReadOnlyList<string> ParseErrors(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new[] { Truncate(body) };
            }

            var messages = new List<string>();

            if (root.TryGetProperty("errors", out var errors))
            {
                CollectErrors(errors, prefix: null, messages);
            }

            if (root.TryGetProperty("error", out var singleError))
            {
                CollectErrors(singleError, prefix: null, messages);
            }

            return messages.Count > 0 ? messages : new[] { Truncate(body) };
        }
        catch (JsonException)
        {
            return new[] { Truncate(body) };
        }
    }

    private static void CollectErrors(JsonElement element, string? prefix, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(prefix is null ? text! : prefix + ": " + text);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrors(item, prefix, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectErrors(property.Value, property.Name, messages);
                }

                break;
        }
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            // A cancellation raised while the caller token is untriggered is the HttpClient timeout.
            OperationCanceledException when cancellationToken.IsCancellationRequested => false,
            OperationCanceledException => true,
            HttpRequestException => true,
            _ => false
        };

    private static bool IsRetryableStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private TimeSpan GetRetryDelay(int attempt, HttpResponseMessage? response)
    {
        // Maxio throttles by concurrency and may tell us how long to stand down; prefer its answer.
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
            {
                return Cap(delta);
            }

            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    return Cap(wait);
                }
            }
        }

        var baseDelay = Math.Max(1, _settings.RetryBaseDelayMilliseconds);
        var exponential = baseDelay * Math.Pow(2, attempt - 1);

        // Full jitter with a floor: spreads retries from parallel callers instead of having them
        // re-collide in lockstep.
        var jittered = Math.Max(baseDelay / 2.0, Random.Shared.NextDouble() * exponential);
        return Cap(TimeSpan.FromMilliseconds(jittered));
    }

    private static TimeSpan Cap(TimeSpan delay) =>
        delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;

    private static string Truncate(string value) =>
        value.Length <= 500 ? value.Trim() : value[..500].Trim() + "...";

    /// <summary>
    /// Drops query-string values before a path reaches a log sink: a customer lookup carries the
    /// shopper's account reference in the query string.
    /// </summary>
    private static string Redact(string path)
    {
        var separator = path.IndexOf('?');
        return separator < 0 ? path : string.Concat(path.AsSpan(0, separator), "?...");
    }

    /// <summary>
    /// Builds the documented Maxio credential: the API key as the HTTP Basic user name, with the
    /// literal password <c>x</c>.
    /// </summary>
    internal static AuthenticationHeaderValue BuildBasicAuthHeader(string apiKey)
    {
        var raw = Encoding.UTF8.GetBytes(apiKey + ":x");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }
}
