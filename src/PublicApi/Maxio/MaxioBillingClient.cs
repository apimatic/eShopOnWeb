using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken);
    Task<MaxioSubscription> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var family = await GetRequiredAsync<MaxioProductFamilyResponse>(
            $"product_families/handle:{Uri.EscapeDataString(_options.ProductFamilyHandle)}.json",
            cancellationToken);

        var result = new List<MaxioProduct>();
        for (var page = 1; ; page++)
        {
            var products = await GetRequiredAsync<List<MaxioProductResponse>>(
                $"product_families/{family.ProductFamily.Id}/products.json?page={page}&per_page=200",
                cancellationToken);

            foreach (var product in products)
            {
                if (product.Product.ArchivedAt is null)
                {
                    result.Add(product.Product);
                }
            }

            if (products.Count < 200)
            {
                break;
            }
        }

        return result;
    }

    public async Task<MaxioCustomer> GetCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetRequiredAsync<MaxioCustomerResponse>(
            $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCreateCustomer customer, CancellationToken cancellationToken)
    {
        var response = await PostRequiredAsync<MaxioCustomerResponse, MaxioCustomerRequest>(
            "customers.json", new MaxioCustomerRequest { Customer = customer }, cancellationToken);
        return response.Customer;
    }

    public async Task<MaxioSubscription> GetSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var response = await GetRequiredAsync<MaxioSubscriptionResponse>(
            $"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}",
            cancellationToken);
        return response.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioCreateSubscription subscription, CancellationToken cancellationToken)
    {
        var response = await PostRequiredAsync<MaxioSubscriptionResponse, MaxioSubscriptionRequest>(
            "subscriptions.json", new MaxioSubscriptionRequest { Subscription = subscription }, cancellationToken);
        return response.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await GetRequiredAsync<List<MaxioSubscriptionResponse>>(
            $"customers/{customerId}/subscriptions.json",
            cancellationToken);

        var result = new List<MaxioSubscription>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            result.Add(subscription.Subscription);
        }

        return result;
    }

    private async Task<T> GetRequiredAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostRequiredAsync<TResponse, TRequest>(
        string path, TRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MaxioApiException(response.StatusCode, body);
        }

        var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (value is null)
        {
            throw new MaxioApiException(response.StatusCode, "Maxio returned an empty or invalid response.");
        }

        return value;
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody)
        : base($"Maxio returned {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}

public sealed class MaxioProductFamilyResponse
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily ProductFamily { get; set; } = new();
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
    [JsonPropertyName("interval")]
    public int Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioCreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("product")]
    public MaxioSubscriptionProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionProduct
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; set; } = string.Empty;
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
