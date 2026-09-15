using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public interface IMaxioClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);

    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);

    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);

    Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken);

    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var path = $"product_families/handle:{Uri.EscapeDataString(productFamilyHandle)}/products.json";
        var products = await SendAsync<List<MaxioProductEnvelope>>(HttpMethod.Get, path, null, cancellationToken);
        return products.ConvertAll(product => product.Product);
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        var path = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await SendOptionalAsync<MaxioCustomerEnvelope>(HttpMethod.Get, path, null, cancellationToken,
            notFoundIsNull: true);
        return response?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, CancellationToken cancellationToken)
    {
        var request = new MaxioCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = reference
            }
        };

        var response = await SendAsync<MaxioCustomerEnvelope>(HttpMethod.Post, "customers.json", request, cancellationToken);
        return response.Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        var subscriptions = await SendAsync<List<MaxioSubscriptionEnvelope>>(
            HttpMethod.Get,
            $"customers/{customerId}/subscriptions.json",
            null,
            cancellationToken);
        return subscriptions.ConvertAll(subscription => subscription.Subscription);
    }

    public async Task<MaxioSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken)
    {
        var response = await SendAsync<MaxioSubscriptionEnvelope>(
            HttpMethod.Get,
            $"subscriptions/{subscriptionId}.json",
            null,
            cancellationToken);
        return response.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, int customerId, string reference, CancellationToken cancellationToken)
    {
        var request = new MaxioSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = productHandle,
                CustomerId = customerId,
                Reference = reference,
                PaymentCollectionMethod = "invoice"
            }
        };

        var response = await SendAsync<MaxioSubscriptionEnvelope>(HttpMethod.Post, "subscriptions.json", request, cancellationToken);
        return response.Subscription;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(method, path, body, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, content);
    }

    private async Task<T?> SendOptionalAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool notFoundIsNull)
    {
        using var response = await SendRequestAsync(method, path, body, cancellationToken, notFoundIsNull);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return default;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new MaxioApiException((int)response.StatusCode, content);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound))
            return response;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;
        response.Dispose();
        throw new MaxioApiException(statusCode, content);
    }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
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

public sealed class MaxioCustomerRequest
{
    [JsonPropertyName("customer")]
    public MaxioCreateCustomer Customer { get; set; } = new();
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public int ProductPricePointId { get; set; }
}

public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

public sealed class MaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}
