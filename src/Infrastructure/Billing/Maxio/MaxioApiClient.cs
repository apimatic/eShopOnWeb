using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Typed <see cref="HttpClient"/> implementation of <see cref="IMaxioApiClient"/>.
/// Authentication (HTTP Basic, api-key:x), base address and JSON handling are configured
/// where the client is registered; this type owns request/response shaping, transient
/// retry, and translation of Maxio error responses into <see cref="MaxioApiException"/>.
/// </summary>
internal sealed class MaxioApiClient : IMaxioApiClient
{
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, IAppLogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Maxio's maximum page size for product listings.
    private const int ProductsPerPage = 200;

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        // products.json lists every product on the site, so page through all results to be
        // sure no plan in the target family is missed on a large catalog.
        var products = new List<MaxioProduct>();
        var page = 1;
        while (true)
        {
            var pageUri = $"products.json?page={page}&per_page={ProductsPerPage}";
            var envelopes = await SendAsync<List<MaxioProductEnvelope>>(
                () => new HttpRequestMessage(HttpMethod.Get, pageUri),
                idempotent: true,
                cancellationToken).ConfigureAwait(false);

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

            if (envelopes.Count < ProductsPerPage)
            {
                break;
            }

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var uri = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";

        // 404 is the documented "no match" outcome for lookup-by-reference; treat as null.
        var (found, envelope) = await SendAllowingNotFoundAsync<MaxioCustomerEnvelope>(
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken).ConfigureAwait(false);

        return found ? envelope?.Customer : null;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        // Idempotent at the business level: Maxio enforces a unique customer reference, so a
        // retried create is safe — a duplicate is rejected with 422 and re-resolved by lookup.
        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            () => JsonRequest(HttpMethod.Post, "customers.json", request),
            idempotent: true,
            cancellationToken).ConfigureAwait(false);

        if (envelope?.Customer is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.OK, Array.Empty<string>(), "Customer creation returned no customer payload.");
        }

        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var envelopes = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            () => new HttpRequestMessage(HttpMethod.Get, $"customers/{customerId}/subscriptions.json"),
            idempotent: true,
            cancellationToken).ConfigureAwait(false);

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

    public async Task<MaxioSubscription> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        // Not marked idempotent: subscription creation has no server-side uniqueness guard,
        // so we must not auto-retry on ambiguous 5xx (which could double-create). Only 429
        // (definitely not processed) is retried inside SendAsync.
        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            () => JsonRequest(HttpMethod.Post, "subscriptions.json", request),
            idempotent: false,
            cancellationToken).ConfigureAwait(false);

        if (envelope?.Subscription is null)
        {
            throw new MaxioApiException((int)HttpStatusCode.Created, Array.Empty<string>(), "Subscription creation returned no subscription payload.");
        }

        return envelope.Subscription;
    }

    private static HttpRequestMessage JsonRequest<TBody>(HttpMethod method, string uri, TBody body)
    {
        return new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };
    }

    private async Task<TResponse?> SendAsync<TResponse>(
        Func<HttpRequestMessage> requestFactory,
        bool idempotent,
        CancellationToken cancellationToken)
    {
        var (_, value) = await SendCoreAsync<TResponse>(requestFactory, idempotent, allowNotFound: false, cancellationToken)
            .ConfigureAwait(false);
        return value;
    }

    private Task<(bool Found, TResponse? Value)> SendAllowingNotFoundAsync<TResponse>(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        return SendCoreAsync<TResponse>(requestFactory, idempotent: true, allowNotFound: true, cancellationToken);
    }

    private async Task<(bool Found, TResponse? Value)> SendCoreAsync<TResponse>(
        Func<HttpRequestMessage> requestFactory,
        bool idempotent,
        bool allowNotFound,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            using var request = requestFactory();
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (idempotent && attempt <= MaxRetries)
            {
                // Transport failure before a response: safe to retry only for idempotent calls.
                await DelayBeforeRetryAsync(attempt, retryAfter: null, ex.Message, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && idempotent && attempt <= MaxRetries)
            {
                await DelayBeforeRetryAsync(attempt, retryAfter: null, "request timed out", cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return (false, default);
                }

                if (response.IsSuccessStatusCode)
                {
                    var value = await ReadJsonAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
                    return (true, value);
                }

                var status = (int)response.StatusCode;
                var shouldRetry = attempt <= MaxRetries && IsTransient(response.StatusCode, idempotent);
                if (shouldRetry)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    await DelayBeforeRetryAsync(attempt, retryAfter, $"HTTP {status}", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var errors = MaxioErrorParser.Parse(body);
                throw new MaxioApiException(status, errors, body);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode, bool idempotent)
    {
        // 429 is always safe to retry (the request was rejected, not processed).
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        // 5xx may have been processed server-side; only retry for idempotent operations.
        return idempotent && (int)statusCode >= 500;
    }

    private async Task DelayBeforeRetryAsync(int attempt, TimeSpan? retryAfter, string reason, CancellationToken cancellationToken)
    {
        var delay = retryAfter ?? TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
        _logger.LogWarning($"Maxio request failed ({reason}); retry {attempt}/{MaxRetries} after {delay.TotalMilliseconds:N0}ms.");
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TResponse?> ReadJsonAsync<TResponse>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new MaxioApiException((int)response.StatusCode, new[] { $"Unable to parse Maxio response: {ex.Message}" }, body);
        }
    }
}
