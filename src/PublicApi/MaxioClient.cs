using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Thin, verified HTTP client for the Maxio Advanced Billing API.</summary>
public sealed class MaxioClient : IMaxioClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly MaxioOptions _options;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var family = Uri.EscapeDataString(_options.ProductFamilyHandle);
        using var response = await _httpClient.GetAsync($"product_families/handle:{family}/products.json?per_page=200", cancellationToken);
        await EnsureSuccessAsync(response, "the subscription plans");
        var payload = await response.Content.ReadFromJsonAsync<List<ProductEnvelope>>(JsonOptions, cancellationToken) ?? [];
        var plans = new List<MaxioPlan>(payload.Count);
        foreach (var item in payload)
        {
            if (item.Product is null || string.IsNullOrWhiteSpace(item.Product.Handle)) continue;
            plans.Add(new MaxioPlan(item.Product.Id, item.Product.Handle, item.Product.Name ?? item.Product.Handle,
                item.Product.PriceInCents, item.Product.Interval, item.Product.IntervalUnit ?? "month", item.Product.ArchivedAt is not null));
        }
        return plans;
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(reference)}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "the customer lookup");
        var payload = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return payload?.Customer is null ? null : ToCustomer(payload.Customer);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreate customer, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("customers.json", new
        {
            customer = new { first_name = customer.FirstName, last_name = customer.LastName, email = customer.Email, reference = customer.Reference }
        }, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "customer creation");
        var payload = await response.Content.ReadFromJsonAsync<CustomerEnvelope>(JsonOptions, cancellationToken);
        return payload?.Customer is null ? throw new MaxioApiException(response.StatusCode, "customer creation") : ToCustomer(payload.Customer);
    }

    public async Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"customers/{customerId}/subscriptions.json", cancellationToken);
        await EnsureSuccessAsync(response, "the customer's subscriptions");
        var payload = await response.Content.ReadFromJsonAsync<List<SubscriptionEnvelope>>(JsonOptions, cancellationToken) ?? [];
        var subscriptions = new List<MaxioSubscription>(payload.Count);
        foreach (var item in payload)
        {
            if (item.Subscription is not null) subscriptions.Add(ToSubscription(item.Subscription));
        }
        return subscriptions;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("subscriptions.json", new
        {
            // The seeded plans intentionally do not require a payment profile. Remittance is the
            // documented card-free collection method for current Relationship Invoicing sites.
            subscription = new { customer_id = customerId, product_handle = productHandle, payment_collection_method = "remittance" }
        }, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, "subscription creation");
        var payload = await response.Content.ReadFromJsonAsync<SubscriptionEnvelope>(JsonOptions, cancellationToken);
        return payload?.Subscription is null ? throw new MaxioApiException(response.StatusCode, "subscription creation") : ToSubscription(payload.Subscription);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode) throw new MaxioApiException(response.StatusCode, operation);
        await Task.CompletedTask;
    }

    private static MaxioCustomer ToCustomer(CustomerJson customer) => new(customer.Id, customer.Reference ?? string.Empty);
    private static MaxioSubscription ToSubscription(SubscriptionJson subscription) => new(
        subscription.Id, subscription.CustomerId, subscription.State ?? "unknown", subscription.ProductHandle ?? subscription.Product?.Handle,
        subscription.Product?.Name, subscription.Product?.PriceInCents, subscription.Product?.Interval, subscription.Product?.IntervalUnit,
        subscription.NextBillingAt, subscription.CurrentPeriodEndsAt);

    private sealed class ProductEnvelope { public ProductJson? Product { get; init; } }
    private sealed class CustomerEnvelope { public CustomerJson? Customer { get; init; } }
    private sealed class SubscriptionEnvelope { public SubscriptionJson? Subscription { get; init; } }
    private sealed class ProductJson
    {
        public int Id { get; init; }
        public string? Handle { get; init; }
        public string? Name { get; init; }
        [JsonPropertyName("price_in_cents")] public int PriceInCents { get; init; }
        public int Interval { get; init; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; init; }
        [JsonPropertyName("archived_at")] public DateTimeOffset? ArchivedAt { get; init; }
    }
    private sealed class CustomerJson { public int Id { get; init; } public string? Reference { get; init; } }
    private sealed class SubscriptionJson
    {
        public int Id { get; init; }
        [JsonPropertyName("customer_id")] public int CustomerId { get; init; }
        public string? State { get; init; }
        [JsonPropertyName("product_handle")] public string? ProductHandle { get; init; }
        public ProductJson? Product { get; init; }
        [JsonPropertyName("next_billing_at")] public DateTimeOffset? NextBillingAt { get; init; }
        [JsonPropertyName("current_period_ends_at")] public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    }
}
