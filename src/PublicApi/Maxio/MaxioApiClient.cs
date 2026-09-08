using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <inheritdoc />
public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioApiClient> _logger;

    public MaxioApiClient(HttpClient httpClient, ILogger<MaxioApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default)
    {
        var envelope = (await GetAsync<MaxioSiteEnvelope>("site.json", cancellationToken))!;
        return envelope.Site;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductFamilyProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken = default)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?per_page=200";
        var envelopes = (await GetAsync<List<MaxioProductEnvelope>>(path, cancellationToken)) ?? new List<MaxioProductEnvelope>();
        return envelopes
            .Select(e => e.Product)
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(
        string customerReference,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}";
        var envelope = await GetAsync<MaxioCustomerEnvelope>(path, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            customer = new
            {
                reference = customerReference,
                email,
                first_name = firstName,
                last_name = lastName
            }
        };

        var envelope = (await PostAsync<MaxioCustomerEnvelope>("customers.json", body, cancellationToken))!;
        return envelope.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"customers/{customerId}/subscriptions.json";
        var envelopes = (await GetAsync<List<MaxioSubscriptionEnvelope>>(path, cancellationToken)) ?? new List<MaxioSubscriptionEnvelope>();
        return envelopes
            .Select(e => e.Subscription)
            .ToList();
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(
        string subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        var path = $"subscriptions/lookup.json?reference={Uri.EscapeDataString(subscriptionReference)}";
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(path, cancellationToken, notFoundReturnsNull: true);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subscriptionReference,
                // The eShop plans are seeded with "payment method not required", so the
                // subscription is enrolled on remittance terms and never blocks on card
                // capture / 3-DS. A stored payment profile can be attached later.
                payment_collection_method = "remittance"
            }
        };

        var envelope = (await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", body, cancellationToken))!;
        return envelope.Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken, bool notFoundReturnsNull = false)
        where T : class
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, path, cancellationToken, notFoundReturnsNull);
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: MaxioJson.Options)
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, path, cancellationToken, notFoundReturnsNull: false);
    }

    private async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken, bool notFoundReturnsNull)
        where T : class
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(responseBody) && notFoundReturnsNull)
            {
                return null;
            }

            return MaxioJson.Deserialize<T>(responseBody);
        }

        if (notFoundReturnsNull && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var statusCode = (int)response.StatusCode;
        _logger.LogWarning(
            "Maxio API call to '{Path}' failed with status {StatusCode}. Body: {Body}",
            path, statusCode, responseBody);

        throw new MaxioApiException(statusCode, responseBody, $"The Maxio API request to '{path}' failed.");
    }
}
