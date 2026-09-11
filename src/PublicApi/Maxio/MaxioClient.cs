using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<int> ResolveProductFamilyIdAsync(CancellationToken ct = default);
    Task<List<MaxioPlan>> ListPlansAsync(CancellationToken ct = default);
    Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken ct = default);
    Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct = default);
    Task<List<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken ct = default);
}

public sealed class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _options;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MaxioClient(HttpClient http, IOptions<MaxioOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<int> ResolveProductFamilyIdAsync(CancellationToken ct = default)
    {
        var handle = _options.ProductFamilyHandle;
        using var resp = await _http.GetAsync($"/product_families.json?handle={Uri.EscapeDataString(handle)}", ct);
        resp.EnsureSuccessStatusCode();
        var list = await resp.Content.ReadFromJsonAsync<List<FamilyWrapper>>(JsonOpts, ct);
        var family = list?.FirstOrDefault(f => f.ProductFamily?.Handle == handle)?.ProductFamily;
        if (family == null) throw new InvalidOperationException($"Maxio family handle not found: {handle}");
        return family.Id;
    }

    public async Task<List<MaxioPlan>> ListPlansAsync(CancellationToken ct = default)
    {
        var familyId = await ResolveProductFamilyIdAsync(ct);
        using var resp = await _http.GetAsync($"/product_families/{familyId}/products.json", ct);
        resp.EnsureSuccessStatusCode();
        var wrappers = await resp.Content.ReadFromJsonAsync<List<ProductWrapper>>(JsonOpts, ct);
        return wrappers?.Select(w => new MaxioPlan(
            w.Product?.Id ?? 0,
            w.Product?.Name ?? "",
            w.Product?.Handle ?? "",
            w.Product?.PriceInCents ?? 0,
            w.Product?.IntervalUnit ?? "month",
            w.Product?.Interval ?? 1,
            w.Product?.RequireCreditCard ?? false
        )).ToList() ?? new();
    }

    public async Task<MaxioCustomer?> LookupCustomerAsync(string reference, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var wrapper = await resp.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOpts, ct);
        return wrapper?.Customer == null ? null : new MaxioCustomer(wrapper.Customer.Id, wrapper.Customer.Email ?? "", wrapper.Customer.FirstName ?? "", wrapper.Customer.LastName ?? "", wrapper.Customer.Reference ?? reference);
    }

    public async Task<MaxioCustomer> CreateCustomerAsync(string reference, string email, string firstName, string lastName, CancellationToken ct = default)
    {
        var request = new CustomerRequest { Customer = new CustomerBody { Reference = reference, Email = email, FirstName = firstName, LastName = lastName } };
        using var resp = await _http.PostAsJsonAsync("/customers.json", request, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var wrapper = await resp.Content.ReadFromJsonAsync<CustomerWrapper>(JsonOpts, ct);
        var c = wrapper?.Customer ?? throw new InvalidOperationException("Maxio create customer returned empty");
        return new MaxioCustomer(c.Id, c.Email ?? "", c.FirstName ?? "", c.LastName ?? "", c.Reference ?? reference);
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, int productId, CancellationToken ct = default)
    {
        var request = new SubscriptionRequest { Subscription = new SubscriptionBody { CustomerId = customerId, ProductId = productId } };
        using var resp = await _http.PostAsJsonAsync("/subscriptions.json", request, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var wrapper = await resp.Content.ReadFromJsonAsync<SubscriptionWrapper>(JsonOpts, ct);
        var s = wrapper?.Subscription ?? throw new InvalidOperationException("Maxio create subscription returned empty");
        return new MaxioSubscription(s.Id, s.State ?? "", s.ProductId ?? 0, s.CustomerId ?? 0, s.NextBillingAt?.ToString("O") ?? "");
    }

    public async Task<List<MaxioSubscription>> ListSubscriptionsAsync(int customerId, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync($"/customers/{customerId}/subscriptions.json", ct);
        resp.EnsureSuccessStatusCode();
        var wrappers = await resp.Content.ReadFromJsonAsync<List<SubscriptionWrapper>>(JsonOpts, ct);
        return wrappers?.Select(w => new MaxioSubscription(
            w.Subscription?.Id ?? 0,
            w.Subscription?.State ?? "",
            w.Subscription?.ProductId ?? 0,
            w.Subscription?.CustomerId ?? 0,
            w.Subscription?.NextBillingAt?.ToString("O") ?? ""
        )).ToList() ?? new();
    }
}

public record MaxioPlan(int Id, string Name, string Handle, long PriceInCents, string IntervalUnit, int Interval, bool RequireCreditCard);
public record MaxioCustomer(int Id, string Email, string FirstName, string LastName, string Reference);
public record MaxioSubscription(int Id, string State, int ProductId, int CustomerId, string NextBillingAt);

// Wrapper classes
public class FamilyWrapper { [JsonPropertyName("product_family")] public Family? ProductFamily { get; set; } }
public class Family { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("handle")] public string? Handle { get; set; } }
public class ProductWrapper { [JsonPropertyName("product")] public Product? Product { get; set; } }
public class Product { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("name")] public string? Name { get; set; } [JsonPropertyName("handle")] public string? Handle { get; set; } [JsonPropertyName("price_in_cents")] public long PriceInCents { get; set; } [JsonPropertyName("interval_unit")] public string? IntervalUnit { get; set; } [JsonPropertyName("interval")] public int Interval { get; set; } [JsonPropertyName("require_credit_card")] public bool RequireCreditCard { get; set; } }
public class CustomerWrapper { [JsonPropertyName("customer")] public Customer? Customer { get; set; } }
public class Customer { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("email")] public string? Email { get; set; } [JsonPropertyName("first_name")] public string? FirstName { get; set; } [JsonPropertyName("last_name")] public string? LastName { get; set; } [JsonPropertyName("reference")] public string? Reference { get; set; } }
public class CustomerBody { [JsonPropertyName("reference")] public string? Reference { get; set; } [JsonPropertyName("email")] public string? Email { get; set; } [JsonPropertyName("first_name")] public string? FirstName { get; set; } [JsonPropertyName("last_name")] public string? LastName { get; set; } }
public class CustomerRequest { [JsonPropertyName("customer")] public CustomerBody? Customer { get; set; } }
public class SubscriptionWrapper { [JsonPropertyName("subscription")] public Subscription? Subscription { get; set; } }
public class Subscription { [JsonPropertyName("id")] public int Id { get; set; } [JsonPropertyName("state")] public string? State { get; set; } [JsonPropertyName("product_id")] public int? ProductId { get; set; } [JsonPropertyName("customer_id")] public int? CustomerId { get; set; } [JsonPropertyName("next_billing_at")] public DateTimeOffset? NextBillingAt { get; set; } }
public class SubscriptionBody { [JsonPropertyName("customer_id")] public int CustomerId { get; set; } [JsonPropertyName("product_id")] public int ProductId { get; set; } }
public class SubscriptionRequest { [JsonPropertyName("subscription")] public SubscriptionBody? Subscription { get; set; } }
