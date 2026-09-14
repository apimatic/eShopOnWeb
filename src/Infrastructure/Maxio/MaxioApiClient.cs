using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin, typed HTTP client over the Maxio Advanced Billing (Chargify) REST API. Handles JSON
/// (de)serialization, transient-retry for safe GETs, and status-code-to-exception mapping.
/// The base address and HTTP Basic auth header are configured on the injected <see cref="HttpClient"/>.
/// </summary>
internal sealed class MaxioApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private const int MaxGetAttempts = 3;

    private readonly HttpClient _http;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient http, ILogger<MaxioApiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Lists all product families on the site so the caller can resolve one by handle.</summary>
    public async Task<IReadOnlyList<MaxioProductFamily>> GetProductFamiliesAsync(CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<ProductFamilyEnvelope>>("product_families.json", cancellationToken);
        return Unwrap(envelopes, e => e.ProductFamily);
    }

    /// <summary>Lists the products (plans) belonging to a product family.</summary>
    public async Task<IReadOnlyList<MaxioProduct>> GetProductsByFamilyIdAsync(int familyId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<ProductEnvelope>>(
            $"product_families/{familyId}/products.json", cancellationToken);
        return Unwrap(envelopes, e => e.Product);
    }

    /// <summary>Looks up a customer by external reference. Returns null when no such customer exists (404).</summary>
    public async Task<MaxioCustomer?> LookupCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken);
        var envelope = await ReadJsonAsync<CustomerEnvelope>(response, cancellationToken);
        return envelope?.Customer;
    }

    /// <summary>Creates a customer. The uniqueness token guards against duplicate submissions.</summary>
    public async Task<MaxioCustomer> CreateCustomerAsync(CustomerAttributes attributes, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateCustomerRequest { Customer = attributes, UniquenessToken = uniquenessToken };
        var envelope = await PostAsync<CreateCustomerRequest, CustomerEnvelope>("customers.json", body, cancellationToken);
        return envelope?.Customer
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "customers.json", "Response did not contain a customer.");
    }

    /// <summary>Lists the subscriptions belonging to a customer.</summary>
    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<SubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", cancellationToken);
        return Unwrap(envelopes, e => e.Subscription);
    }

    /// <summary>Creates a subscription. The uniqueness token guards against duplicate submissions.</summary>
    public async Task<MaxioSubscription> CreateSubscriptionAsync(SubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken)
    {
        var body = new CreateSubscriptionRequest { Subscription = attributes, UniquenessToken = uniquenessToken };
        var envelope = await PostAsync<CreateSubscriptionRequest, SubscriptionEnvelope>("subscriptions.json", body, cancellationToken);
        return envelope?.Subscription
            ?? throw new MaxioApiException(HttpStatusCode.OK, "POST", "subscriptions.json", "Response did not contain a subscription.");
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Get.Method, path, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        // POSTs are not retried: they are not idempotent at the transport level; idempotency is
        // instead enforced by the uniqueness_token carried in the body and by pre-checks upstream.
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, HttpMethod.Post.Method, path, cancellationToken);
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await _http.SendAsync(request, cancellationToken);
                if (attempt < MaxGetAttempts && IsTransient(response.StatusCode))
                {
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < MaxGetAttempts)
            {
                _logger.LogWarning(ex, "Transient error calling Maxio (attempt {Attempt}/{Max}); retrying.", attempt, MaxGetAttempts);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private static bool IsTransient(HttpStatusCode status)
        => status == HttpStatusCode.RequestTimeout
           || status == HttpStatusCode.TooManyRequests
           || (int)status >= 500;

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string method, string path, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await SafeReadBodyAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict
            && body is not null
            && body.Contains("Duplicate", StringComparison.OrdinalIgnoreCase))
        {
            throw new MaxioDuplicateSubmissionException(method, path, body);
        }

        throw new MaxioApiException(response.StatusCode, method, path, body);
    }

    private async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent
            || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<TItem> Unwrap<TEnvelope, TItem>(List<TEnvelope>? envelopes, Func<TEnvelope, TItem?> selector)
        where TItem : class
    {
        if (envelopes is null || envelopes.Count == 0)
        {
            return Array.Empty<TItem>();
        }

        var items = new List<TItem>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            var item = selector(envelope);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return items;
    }
}
