using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Typed HTTP client for the Maxio Billing API. Authentication, base address, timeout and retry are
/// configured on the underlying <see cref="HttpClient"/> (see <c>MaxioServiceCollectionExtensions</c>);
/// this class only shapes requests and translates responses.
/// </summary>
public class MaxioApiClient : IMaxioApiClient
{
    // The API caps per_page at 200; asking for the maximum keeps the catalog to a single round trip.
    private const int MaxPageSize = 200;
    private const int MaxCatalogPages = 25;

    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite?> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", "read site", cancellationToken).ConfigureAwait(false);
        return envelope?.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsForFamilyAsync(string productFamilyHandle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productFamilyHandle))
        {
            throw new ArgumentException("A product family handle is required.", nameof(productFamilyHandle));
        }

        var products = new List<MaxioProduct>();

        for (var page = 1; page <= MaxCatalogPages; page++)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json" +
                       $"?per_page={MaxPageSize}&page={page.ToString(CultureInfo.InvariantCulture)}";

            var batch = await GetAsync<List<MaxioProductEnvelope>>(
                path, $"list products for family '{productFamilyHandle}'", cancellationToken).ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var envelope in batch)
            {
                if (envelope.Product is not null)
                {
                    products.Add(envelope.Product);
                }
            }

            if (batch.Count < MaxPageSize)
            {
                break;
            }
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(
            path, $"look up customer '{reference}'", cancellationToken, treatNotFoundAsNull: true).ConfigureAwait(false);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerAttributes attributes, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "customers.json")
        {
            Content = JsonContent.Create(new MaxioCreateCustomerRequest(attributes), options: MaxioJson.Options)
        };

        var envelope = await SendAsync<MaxioCustomerEnvelope>(
            request, $"create customer '{attributes.Reference}'", cancellationToken).ConfigureAwait(false);

        return envelope?.Customer
               ?? throw new MaxioApiException(HttpStatusCode.OK, "create customer", new[] { "response contained no customer" });
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(
            path, $"look up subscription '{reference}'", cancellationToken, treatNotFoundAsNull: true).ConfigureAwait(false);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionAttributes attributes, string uniquenessToken, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "subscriptions.json")
        {
            Content = JsonContent.Create(
                new MaxioCreateSubscriptionRequest(attributes, uniquenessToken), options: MaxioJson.Options)
        };

        // The uniqueness token makes this POST replayable: a duplicate is rejected with 409 rather
        // than creating a second subscription, so the transport layer may retry it.
        request.Options.Set(MaxioResilienceHandler.RetrySafeOption, true);

        var envelope = await SendAsync<MaxioSubscriptionEnvelope>(
            request, $"create subscription '{attributes.Reference}'", cancellationToken).ConfigureAwait(false);

        return envelope?.Subscription
               ?? throw new MaxioApiException(HttpStatusCode.OK, "create subscription", new[] { "response contained no subscription" });
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId.ToString(CultureInfo.InvariantCulture)}/subscriptions.json";
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            path, $"list subscriptions for customer {customerId}", cancellationToken, treatNotFoundAsNull: true).ConfigureAwait(false);

        if (envelopes is null)
        {
            return Array.Empty<MaxioSubscription>();
        }

        var subscriptions = new List<MaxioSubscription>(envelopes.Count);
        foreach (var envelope in envelopes)
        {
            if (envelope.Subscription is not null)
            {
                subscriptions.Add(envelope.Subscription);
            }
        }

        return subscriptions;
    }

    private Task<T?> GetAsync<T>(string path, string description, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
        where T : class =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), description, cancellationToken, treatNotFoundAsNull);

    private async Task<T?> SendAsync<T>(HttpRequestMessage request, string description, CancellationToken cancellationToken, bool treatNotFoundAsNull = false)
        where T : class
    {
        using (request)
        {
            HttpResponseMessage response;
            try
            {
                // Responses are small JSON documents, so buffering them keeps the body available
                // after the per-attempt timeout scope in the resilience handler has closed.
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException ||
                                       (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // Unreachable, or too slow and the overall budget ran out — as opposed to the caller
                // cancelling, which must keep propagating. Surfaced as an upstream 503 so callers can
                // tell a provider outage apart from a request we got wrong.
                throw new MaxioApiException(HttpStatusCode.ServiceUnavailable, description, new[] { ex.Message });
            }

            using (response)
            {
                return await ReadResponseAsync<T>(response, description, treatNotFoundAsNull, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<T?> ReadResponseAsync<T>(
        HttpResponseMessage response,
        string description,
        bool treatNotFoundAsNull,
        CancellationToken cancellationToken)
        where T : class
    {
        if (treatNotFoundAsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Maxio {Description} returned 404; treating as absent.", description);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errors = await MaxioErrorReader.ReadAsync(response, cancellationToken).ConfigureAwait(false);
            throw new MaxioApiException(response.StatusCode, description, errors);
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, MaxioJson.Options, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new MaxioApiException(response.StatusCode, description,
                new[] { "response body could not be parsed: " + ex.Message });
        }
    }
}
