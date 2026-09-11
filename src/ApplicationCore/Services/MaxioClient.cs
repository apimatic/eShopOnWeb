using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioClient
{
    Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest req, CancellationToken ct = default);
    Task<List<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest req, CancellationToken ct = default);
    Task<List<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken ct = default);
}

public record MaxioCustomer(int Id, string Reference, string Email, string FirstName, string LastName, string Organization);
public record MaxioSubscription(int Id, string State, string ProductHandle, string ProductName, decimal PriceInCents, DateTime? NextBillingDate, string CustomerReference);
public record MaxioProduct(int Id, string Handle, string Name, decimal PriceInCents, int Interval, string IntervalUnit);

public record MaxioCustomerCreateRequest(string Reference, string Email, string FirstName, string LastName, string Organization = "", string Country = "US");
public record MaxioSubscriptionCreateRequest(string CustomerReference, string ProductHandle, string? ProductPricePointHandle = null);

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl => _settings.BaseUrl
        ?? $"https://{_settings.Subdomain}.chargify.com";

    private HttpClient Client
    {
        get
        {
            if (string.IsNullOrEmpty(_http.DefaultRequestHeaders.Authorization?.Parameter))
            {
                var auth = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
                _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", auth);
            }
            return _http;
        }
    }

    public async Task<MaxioCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var resp = await Client.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadFromJsonAsync<CustomerLookupResponse>(cancellationToken: ct);
            if (doc?.Customer == null) return null;
            return Map(doc.Customer);
        }
        catch { return null; }
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(MaxioCustomerCreateRequest req, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/customers.json";
        var payload = new { customer = new { reference = req.Reference, email = req.Email, first_name = req.FirstName, last_name = req.LastName, organization = req.Organization, country = req.Country } };
        var resp = await Client.PostAsJsonAsync(url, payload, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<CustomerResponse>(cancellationToken: ct);
        if (doc?.Customer == null) throw new InvalidOperationException("Customer creation returned no customer.");
        return Map(doc.Customer);
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/subscriptions.json?customer_reference={Uri.EscapeDataString(customerReference)}";
        var resp = await Client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<SubscriptionListResponse>(cancellationToken: ct);
        return doc?.Subscriptions?.Select(MapSub).ToList() ?? new List<MaxioSubscription>();
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(MaxioSubscriptionCreateRequest req, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/subscriptions.json";
        var payload = new { subscription = new { product_handle = req.ProductHandle, customer_reference = req.CustomerReference, product_price_point_handle = req.ProductPricePointHandle } };
        var resp = await Client.PostAsJsonAsync(url, payload, ct);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<SubscriptionResponse>(cancellationToken: ct);
        if (doc?.Subscription == null) throw new InvalidOperationException("Subscription creation returned no subscription.");
        return MapSub(doc.Subscription);
    }

    public async Task<List<MaxioProduct>> ListProductsForFamilyAsync(string familyHandle, CancellationToken ct = default)
    {
        var familyUrl = $"{BaseUrl}/product_families/handle:{familyHandle}.json";
        // First get family id from handle
        // We can query by handle directly on products endpoint: /product_families/handle:{handle}/products.json
        var url = $"{BaseUrl}/product_families/handle:{familyHandle}/products.json?per_page=200";
        var resp = await Client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return new List<MaxioProduct>();
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<ProductListResponse>(cancellationToken: ct);
        return doc?.Products?.Select(p => new MaxioProduct(p.Id, p.Handle ?? "", p.Name, (decimal)(p.PriceInCents / 100.0), p.Interval, p.IntervalUnit)).ToList() ?? new List<MaxioProduct>();
    }

    private MaxioCustomer Map(CustomerData c) => new(c.Id, c.Reference ?? "", c.Email ?? "", c.FirstName ?? "", c.LastName ?? "", c.Organization ?? "");
    private MaxioSubscription MapSub(SubscriptionData s) => new(s.Id, s.State ?? "", s.Product?.Handle ?? "", s.Product?.Name ?? "", (decimal)(s.ProductPriceInCents ?? 0) / 100m, s.NextAssessmentAt ?? s.CurrentPeriodEndsAt, s.CustomerReference ?? "");

    // Response DTOs
    private class CustomerLookupResponse { [JsonPropertyName("customer")] public CustomerData? Customer { get; set; } }
    private class CustomerResponse { [JsonPropertyName("customer")] public CustomerData? Customer { get; set; } }
    private class SubscriptionResponse { [JsonPropertyName("subscription")] public SubscriptionData? Subscription { get; set; } }
    private class SubscriptionListResponse { [JsonPropertyName("subscriptions")] public List<SubscriptionData>? Subscriptions { get; set; } }
    private class ProductListResponse { [JsonPropertyName("products")] public List<ProductData>? Products { get; set; } }

    private class CustomerData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("reference")] public string? Reference { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
        [JsonPropertyName("organization")] public string? Organization { get; set; }
    }

    private class SubscriptionData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("customer_reference")] public string? CustomerReference { get; set; }
        [JsonPropertyName("product_price_in_cents")] public int? ProductPriceInCents { get; set; }
        [JsonPropertyName("next_assessment_at")] public DateTime? NextAssessmentAt { get; set; }
        [JsonPropertyName("current_period_ends_at")] public DateTime? CurrentPeriodEndsAt { get; set; }
        [JsonPropertyName("product")] public ProductData? Product { get; set; }
    }

    private class ProductData
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("handle")] public string? Handle { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
        [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; }
    }
}
