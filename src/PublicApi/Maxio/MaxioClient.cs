using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Small, typed HTTP client for the Advanced Billing API operations used by subscriptions.</summary>
public sealed class MaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/handle:{family}/products.json", cancellationToken);
        await EnsureSuccessAsync(response);
        var products = await response.Content.ReadFromJsonAsync<List<MaxioProductEnvelope>>(cancellationToken: cancellationToken);
        return (products ?? new List<MaxioProductEnvelope>()).ConvertAll(item => item.Product);
    }

    public async Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer ?? throw new InvalidOperationException("Maxio returned an empty customer response.");
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerInput customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync($"customers.json?uniqueness_token={Uri.EscapeDataString(uniquenessToken)}", new { customer }, cancellationToken);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioCustomerEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Customer ?? throw new InvalidOperationException("Maxio returned an empty customer response.");
    }

    public async Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"subscriptions/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription ?? throw new InvalidOperationException("Maxio returned an empty subscription response.");
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, string customerReference, string reference, string uniquenessToken, CancellationToken cancellationToken)
    {
        var subscription = new MaxioSubscriptionInput(productHandle, customerReference, reference);
        using var response = await _httpClient.PostAsJsonAsync($"subscriptions.json?uniqueness_token={Uri.EscapeDataString(uniquenessToken)}", new { subscription }, cancellationToken);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<MaxioSubscriptionEnvelope>(cancellationToken: cancellationToken);
        return envelope?.Subscription ?? throw new InvalidOperationException("Maxio returned an empty subscription response.");
    }

    public async Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response);
        var subscriptions = await response.Content.ReadFromJsonAsync<List<MaxioSubscriptionEnvelope>>(cancellationToken: cancellationToken);
        return (subscriptions ?? new List<MaxioSubscriptionEnvelope>()).ConvertAll(item => item.Subscription);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Consume the body before disposal without allowing a provider response to leak through our API.
            _ = await response.Content.ReadAsStringAsync();
            throw new MaxioApiException(response.StatusCode);
        }
    }
}

public sealed record MaxioCustomerInput(
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record MaxioSubscriptionInput(
    [property: JsonPropertyName("product_handle")] string ProductHandle,
    [property: JsonPropertyName("customer_reference")] string CustomerReference,
    [property: JsonPropertyName("reference")] string Reference,
    // The seeded catalog permits enrollment without a card. Remittance prevents Advanced Billing
    // from attempting an immediate automatic collection during that cardless enrollment.
    [property: JsonPropertyName("payment_collection_method")] string PaymentCollectionMethod = "remittance");

public sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
}

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long? ProductPriceInCents { get; init; }
    [JsonPropertyName("next_billing_at")] public DateTimeOffset? NextBillingAt { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
}
