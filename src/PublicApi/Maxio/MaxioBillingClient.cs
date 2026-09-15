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

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, string uniquenessToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken);
}

public sealed class MaxioBillingClient : IMaxioBillingClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioBillingClient(HttpClient httpClient, Microsoft.Extensions.Options.IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MaxioProduct>> ListProductsAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString($"handle:{_options.ProductFamilyHandle}");
        using var response = await _httpClient.GetAsync($"product_families/{family}/products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<List<MaxioProductEnvelope>>(response, cancellationToken);
        return payload.Select(item => item.Product).Where(product => product.ArchivedAt is null).ToList();
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerDraft customer, string uniquenessToken, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", new { customer, uniqueness_token = uniquenessToken }, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeAsync<MaxioCustomerEnvelope>(response, cancellationToken)).Customer;
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var payload = await DeserializeAsync<List<MaxioSubscriptionEnvelope>>(response, cancellationToken);
        return payload.Select(item => item.Subscription).ToList();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(long customerId, string productHandle, string reference, string uniquenessToken, CancellationToken cancellationToken)
    {
        // Invoice collection is intentional for this cardless checkout flow. The
        // seeded plans permit it, and Maxio records the recurring schedule.
        var request = new { subscription = new { customer_id = customerId, product_handle = productHandle, reference, payment_collection_method = "invoice" }, uniqueness_token = uniquenessToken };
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await DeserializeAsync<MaxioSubscriptionEnvelope>(response, cancellationToken)).Subscription;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return value ?? throw new MaxioApiException(HttpStatusCode.BadGateway, "Maxio returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Upstream bodies can contain billing data and are never exposed to callers.
        throw new MaxioApiException(response.StatusCode, $"Maxio returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
    }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
    public HttpStatusCode StatusCode { get; }
}

public sealed class MaxioCustomerDraft
{
    [JsonPropertyName("first_name")] public string FirstName { get; init; } = string.Empty;
    [JsonPropertyName("last_name")] public string LastName { get; init; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;
    [JsonPropertyName("reference")] public string Reference { get; init; } = string.Empty;
}

public sealed class MaxioCustomerEnvelope { [JsonPropertyName("customer")] public MaxioCustomer Customer { get; init; } = new(); }
public sealed class MaxioProductEnvelope { [JsonPropertyName("product")] public MaxioProduct Product { get; init; } = new(); }
public sealed class MaxioSubscriptionEnvelope { [JsonPropertyName("subscription")] public MaxioSubscription Subscription { get; init; } = new(); }

public sealed class MaxioCustomer { [JsonPropertyName("id")] public long Id { get; init; } }

public sealed class MaxioProduct
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("handle")] public string? Handle { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")] public long PriceInCents { get; init; }
    [JsonPropertyName("interval")] public int Interval { get; init; }
    [JsonPropertyName("interval_unit")] public string IntervalUnit { get; init; } = string.Empty;
    [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")] public long Id { get; init; }
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")] public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")] public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("product")] public MaxioProduct? Product { get; init; }
}
