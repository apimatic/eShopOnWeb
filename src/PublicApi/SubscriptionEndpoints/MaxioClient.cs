using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, string uniquenessToken, CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreate subscription, string uniquenessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
}

public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public MaxioClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken)
    {
        var envelope = await GetAsync<MaxioSiteEnvelope>("site.json", false, cancellationToken);
        return envelope?.Site ?? throw new MaxioApiException(HttpStatusCode.OK, "Maxio returned an empty site response.");
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{productFamilyHandle}");
        var envelopes = await GetAsync<List<MaxioProductEnvelope>>(
            $"product_families/{family}/products.json?per_page=200", false, cancellationToken);

        return envelopes?.ConvertAll(item => item.Product) ?? new List<MaxioProduct>();
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString("customers/lookup.json", "reference", reference);
        var envelope = await GetAsync<MaxioCustomerEnvelope>(uri, true, cancellationToken);
        return envelope?.Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(
        MaxioCustomerCreate customer,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioCustomerEnvelope>("customers.json", new
        {
            customer,
            uniqueness_token = uniquenessToken
        }, cancellationToken);

        return envelope.Customer;
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString("subscriptions/lookup.json", "reference", reference);
        var envelope = await GetAsync<MaxioSubscriptionEnvelope>(uri, true, cancellationToken);
        return envelope?.Subscription;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(
        MaxioSubscriptionCreate subscription,
        string uniquenessToken,
        CancellationToken cancellationToken)
    {
        var envelope = await PostAsync<MaxioSubscriptionEnvelope>("subscriptions.json", new
        {
            subscription,
            uniqueness_token = uniquenessToken
        }, cancellationToken);

        return envelope.Subscription;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(
        long customerId,
        CancellationToken cancellationToken)
    {
        var envelopes = await GetAsync<List<MaxioSubscriptionEnvelope>>(
            $"customers/{customerId}/subscriptions.json", false, cancellationToken);

        return envelopes?.ConvertAll(item => item.Subscription) ?? new List<MaxioSubscription>();
    }

    private async Task<T?> GetAsync<T>(string requestUri, bool allowNotFound, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private async Task<T> PostAsync<T>(string requestUri, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(requestUri, body, SerializerOptions, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new MaxioApiException(response.StatusCode, GetErrorMessage(error));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return value ?? throw new MaxioApiException(response.StatusCode, "Maxio returned an empty response.");
    }

    private static string GetErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Maxio returned an error without a response body.";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                return errors.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to a bounded plain-text response.
        }

        return body.Length <= 1000 ? body : body[..1000];
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseMessage)
        : base($"Maxio request failed with status {(int)statusCode}: {responseMessage}")
    {
        StatusCode = statusCode;
        ResponseMessage = responseMessage;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseMessage { get; }
}

public sealed record MaxioCustomerCreate(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record MaxioSubscriptionCreate(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_reference")] string CustomerReference,
    [property: JsonPropertyName("reference")] string Reference,
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod);

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioSiteEnvelope
{
    [JsonPropertyName("site")]
    public MaxioSite Site { get; set; } = new();
}

public sealed class MaxioSite
{
    [JsonPropertyName("relationship_invoicing_enabled")]
    public bool RelationshipInvoicingEnabled { get; set; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

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
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
