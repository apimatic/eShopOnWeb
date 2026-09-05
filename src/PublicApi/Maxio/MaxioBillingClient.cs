using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Small, typed adapter for the operations in maxio-spec/openapi.yaml used by eShopOnWeb.
/// The adapter intentionally models the specification's JSON envelopes and paths directly.
/// </summary>
public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private const int PageSize = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioBillingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(
        string productFamilyHandle,
        CancellationToken cancellationToken)
    {
        var products = new List<MaxioProduct>();
        var page = 1;

        while (true)
        {
            var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json?page={page}&per_page={PageSize}";
            var pageProducts = await GetAsync<List<MaxioProductResponse>>(path, "listProductsForProductFamily", cancellationToken);
            if (pageProducts is null || pageProducts.Count == 0)
                break;

            foreach (var productResponse in pageProducts)
            {
                if (productResponse.Product is not null)
                    products.Add(productResponse.Product);
            }

            if (pageProducts.Count < PageSize)
                break;

            page++;
        }

        return products;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            "readCustomerByReference",
            cancellationToken,
            notFoundIsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioCustomerResponse>("customers.json", new MaxioCreateCustomerRequest { Customer = customer }, "createCustomer", cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<List<MaxioSubscriptionResponse>>($"customers/{customerId}/subscriptions.json", "listCustomerSubscriptions", cancellationToken);
        var subscriptions = new List<MaxioSubscription>();
        if (response is null)
            return subscriptions;

        foreach (var item in response)
        {
            if (item.Subscription is not null)
                subscriptions.Add(item.Subscription);
        }

        return subscriptions;
    }

    public async Task<MaxioSubscription?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            "findSubscription",
            cancellationToken,
            notFoundIsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription?> ReadSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<MaxioSubscriptionResponse>($"subscriptions/{subscriptionId}.json", "readSubscription", cancellationToken, notFoundIsNull: true);
        return response?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await PostAsync<MaxioSubscriptionResponse>("subscriptions.json", new MaxioCreateSubscriptionRequest { Subscription = subscription }, "createSubscription", cancellationToken);
        return response.Subscription;
    }

    private async Task<T?> GetAsync<T>(string path, string operation, CancellationToken cancellationToken, bool notFoundIsNull = false)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
            return default;

        await EnsureSuccessAsync(response, operation);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string path, object body, string operation, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, operation);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new MaxioApiException((int)response.StatusCode, operation);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException((int)response.StatusCode, operation);
        }
    }
}
