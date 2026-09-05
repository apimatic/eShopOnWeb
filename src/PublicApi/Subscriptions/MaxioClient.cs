using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Thin, contract-specific client for Maxio Advanced Billing's HTTP API.
/// </summary>
public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var familyIdentifier = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        var family = await GetAsync<MaxioProductFamilyResponse>($"product_families/{familyIdentifier}.json", cancellationToken);
        if (family.ProductFamily is null)
        {
            throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an invalid product-family response.");
        }

        var products = await GetAsync<List<MaxioProductResponse>>($"product_families/{family.ProductFamily.Id}/products.json", cancellationToken);
        return products
            .Select(item => item.Product)
            .Where(product => product is not null && product.ArchivedAt is null)
            .Cast<MaxioProduct>()
            .Where(product => string.Equals(product.ProductFamily?.Handle, _options.ProductFamilyHandle, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var customer = await ReadResponseAsync<MaxioCustomerResponse>(response, cancellationToken);
        return customer.Customer ?? throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var request = new CreateCustomerRequest(new CreateCustomer(firstName, lastName, email, reference));
        using var response = await _httpClient.PostAsJsonAsync("customers.json", request, JsonOptions, cancellationToken);
        var customer = await ReadResponseAsync<MaxioCustomerResponse>(response, cancellationToken);
        return customer.Customer ?? throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an invalid customer response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetAsync<List<MaxioSubscriptionResponse>>($"customers/{customerId}/subscriptions.json", cancellationToken);
        return subscriptions
            .Select(item => item.Subscription)
            .Where(subscription => subscription is not null)
            .Cast<MaxioSubscription>()
            .ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, CancellationToken cancellationToken)
    {
        // The eShop plans intentionally do not capture a payment method. Remittance is the
        // documented Maxio collection mode for an invoice-backed subscription without a card.
        var request = new CreateSubscriptionRequest(new CreateSubscription(customerId, productHandle, reference, "remittance"));
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, JsonOptions, cancellationToken);
        var subscription = await ReadResponseAsync<MaxioSubscriptionResponse>(response, cancellationToken);
        return subscription.Subscription ?? throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an invalid subscription response.");
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioIntegrationException(response.StatusCode, $"Maxio Advanced Billing request failed with HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new MaxioIntegrationException(HttpStatusCode.BadGateway, "Maxio returned an invalid JSON response.", exception);
        }
    }

    private sealed record CreateCustomerRequest([property: JsonPropertyName("customer")] CreateCustomer Customer);
    private sealed record CreateCustomer(
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName,
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("reference")] string Reference);
    private sealed record CreateSubscriptionRequest([property: JsonPropertyName("subscription")] CreateSubscription Subscription);
    private sealed record CreateSubscription(
        [property: JsonPropertyName("customer_id")] long CustomerId,
        [property: JsonPropertyName("product_handle")] string ProductHandle,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);
}

public sealed class MaxioIntegrationException : Exception
{
    public MaxioIntegrationException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
