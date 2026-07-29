using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Default <see cref="IMaxioClient"/> backed by a typed <see cref="HttpClient"/>. The client's
/// <see cref="HttpClient.BaseAddress"/> and Basic authentication header are configured once at
/// registration time (see <c>MaxioServiceCollectionExtensions</c>).
/// </summary>
public class MaxioClient : IMaxioClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _json;
    private readonly IAppLogger<MaxioClient> _logger;

    public MaxioClient(HttpClient httpClient, JsonSerializerOptions jsonOptions, IAppLogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _json = jsonOptions;
        _logger = logger;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), retryOn5xx: true, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(_json, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateCustomerRequest { Customer = attributes };
        // Non-idempotent create: retry only on 429 (request never reached processing), never on 5xx.
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "customers.json") { Content = JsonContent.Create(body, options: _json) },
            retryOn5xx: false, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(_json, cancellationToken);
        return envelope?.Customer ?? throw new MaxioApiException(response.StatusCode, null, "Maxio returned an empty customer payload.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), retryOn5xx: true, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(_json, cancellationToken);

        var products = new List<MaxioProduct>();
        if (envelopes != null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Product != null)
                {
                    products.Add(envelope.Product);
                }
            }
        }
        return products;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, path), retryOn5xx: true, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelopes = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(_json, cancellationToken);

        var subscriptions = new List<MaxioSubscription>();
        if (envelopes != null)
        {
            foreach (var envelope in envelopes)
            {
                if (envelope.Subscription != null)
                {
                    subscriptions.Add(envelope.Subscription);
                }
            }
        }
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var body = new MaxioCreateSubscriptionRequest { Subscription = attributes, UniquenessToken = uniquenessToken };
        // Non-idempotent create: retry only on 429. The uniqueness_token means a 429-retry that races a
        // successful original is safely rejected with 409, which the service layer reconciles.
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "subscriptions.json") { Content = JsonContent.Create(body, options: _json) },
            retryOn5xx: false, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(_json, cancellationToken);
        return envelope?.Subscription ?? throw new MaxioApiException(response.StatusCode, null, "Maxio returned an empty subscription payload.");
    }

    /// <summary>
    /// Sends a request, retrying transient failures. Retries on HTTP 429 (rate limit — the request was
    /// queued/not processed, so it is always safe to retry) and, when <paramref name="retryOn5xx"/> is set,
    /// on 5xx responses. A fresh <see cref="HttpRequestMessage"/> is built per attempt.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, bool retryOn5xx, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();

            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning($"Maxio request to {request.RequestUri} failed (attempt {attempt}/{MaxAttempts}): {ex.Message}. Retrying...");
                await DelayForAttemptAsync(attempt, cancellationToken);
                continue;
            }

            var shouldRetry = attempt < MaxAttempts &&
                (response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (retryOn5xx && (int)response.StatusCode >= 500));

            if (!shouldRetry)
            {
                return response;
            }

            _logger.LogWarning($"Maxio request to {request.RequestUri} returned {(int)response.StatusCode} (attempt {attempt}/{MaxAttempts}). Retrying...");
            await DelayForAttemptAsync(attempt, cancellationToken);
        }
    }

    private static Task DelayForAttemptAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            // Best-effort: the status code alone is enough to raise a meaningful error.
        }

        throw new MaxioApiException(response.StatusCode, body,
            $"Maxio API request failed with status {(int)response.StatusCode} ({response.StatusCode}).");
    }
}
