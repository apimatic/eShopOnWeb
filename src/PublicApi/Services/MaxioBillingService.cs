using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "";
    public int Interval { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public int CustomerId { get; set; }
}

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync();
    Task<SubscriptionDto?> SubscribeAsync(string userId, string email, string planHandle);
    Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _client;
    private readonly MaxioOptions _opts;
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MaxioBillingService(IOptions<MaxioOptions> opts, HttpClient? client = null)
    {
        _opts = opts.Value;
        _client = client ?? new HttpClient();
        var baseUrl = !string.IsNullOrWhiteSpace(_opts.BaseUrl) ? _opts.BaseUrl.TrimEnd('/') : $"https://{_opts.Subdomain}.chargify.com";
        _client.BaseAddress = new Uri(baseUrl);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_opts.ApiKey}:")));
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        // Look up family by handle, then products
        var familyResp = await _client.GetAsync($"/product_families.json?handle={_opts.ProductFamilyHandle}");
        familyResp.EnsureSuccessStatusCode();
        var familyDoc = await familyResp.Content.ReadAsStringAsync();
        var families = JsonSerializer.Deserialize<List<ProductFamilyWrapper>>(familyDoc, _jsonOpts) ?? new();
        var familyId = families.FirstOrDefault()?.ProductFamily?.Id;
        if (familyId == null) return new();

        var prodResp = await _client.GetAsync($"/products.json?product_family_id={familyId}");
        prodResp.EnsureSuccessStatusCode();
        var prodDoc = await prodResp.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<List<ProductWrapper>>(prodDoc, _jsonOpts) ?? new();
        return products.Select(p => new SubscriptionPlanDto
        {
            Handle = p.Product?.Handle ?? "",
            Name = p.Product?.Name ?? "",
            PriceInCents = p.Product?.PriceInCents ?? 0,
            IntervalUnit = p.Product?.IntervalUnit ?? "month",
            Interval = p.Product?.Interval ?? 1
        }).ToList();
    }

    public async Task<SubscriptionDto?> SubscribeAsync(string userId, string email, string planHandle)
    {
        // Idempotent customer lookup by reference = userId
        var existingCustomer = await FindCustomerByReferenceAsync(userId);
        int customerId;
        if (existingCustomer != null)
        {
            customerId = existingCustomer.Customer?.Id ?? 0;
        }
        else
        {
            var payload = new
            {
                customer = new
                {
                    email,
                    first_name = "Shopper",
                    last_name = "User",
                    reference = userId
                }
            };
            var createResp = await _client.PostAsJsonAsync("/customers.json", payload);
            if (!createResp.IsSuccessStatusCode)
            {
                // Treat duplicate reference as existing (idempotent)
                existingCustomer = await FindCustomerByReferenceAsync(userId);
                if (existingCustomer != null)
                {
                    customerId = existingCustomer.Customer?.Id ?? 0;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var createDoc = await createResp.Content.ReadAsStringAsync();
                var created = JsonSerializer.Deserialize<CustomerResponse>(createDoc, _jsonOpts);
                customerId = created?.Customer?.Id ?? 0;
            }
        }

        // Create subscription using product handle; no card required per seed notes
        var subPayload = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = userId,
                customer_attributes = new
                {
                    email,
                    reference = userId
                }
            }
        };

        var subResp = await _client.PostAsJsonAsync("/subscriptions.json", subPayload);
        subResp.EnsureSuccessStatusCode();
        var subDoc = await subResp.Content.ReadAsStringAsync();
        var sub = JsonSerializer.Deserialize<SubscriptionResponse>(subDoc, _jsonOpts);
        return sub?.Subscription == null ? null : new SubscriptionDto
        {
            Id = sub.Subscription.Id,
            ProductHandle = sub.Subscription.ProductHandle ?? planHandle,
            State = sub.Subscription.State ?? "active",
            CurrentPeriodStartedAt = sub.Subscription.CurrentPeriodStartedAt,
            NextBillingAt = sub.Subscription.NextBillingAt,
            CustomerId = sub.Subscription.CustomerId ?? customerId
        };
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string userId)
    {
        var customer = await FindCustomerByReferenceAsync(userId);
        if (customer == null) return new();
        var resp = await _client.GetAsync($"/subscriptions.json?customer_id={customer.Customer?.Id ?? 0}");
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync();
        var subs = JsonSerializer.Deserialize<List<SubscriptionWrapper>>(doc, _jsonOpts) ?? new();
        return subs.Select(s => new SubscriptionDto
        {
            Id = s.Subscription?.Id ?? 0,
            ProductHandle = s.Subscription?.ProductHandle ?? "",
            State = s.Subscription?.State ?? "",
            CurrentPeriodStartedAt = s.Subscription?.CurrentPeriodStartedAt,
            NextBillingAt = s.Subscription?.NextBillingAt,
            CustomerId = s.Subscription?.CustomerId ?? customer.Customer?.Id ?? 0
        }).ToList();
    }

    private async Task<CustomerWrapper?> FindCustomerByReferenceAsync(string reference)
    {
        try
        {
            var resp = await _client.GetAsync($"/customers.json?reference={reference}");
            resp.EnsureSuccessStatusCode();
            var doc = await resp.Content.ReadAsStringAsync();
            // If exact match returned as object
            var obj = JsonSerializer.Deserialize<CustomerResponse>(doc, _jsonOpts);
            if (obj?.Customer != null) return new CustomerWrapper { Customer = obj.Customer };
            var list = JsonSerializer.Deserialize<List<CustomerResponse>>(doc, _jsonOpts);
            if (list != null && list.Count > 0) return new CustomerWrapper { Customer = list[0].Customer };
        }
        catch { }
        return null;
    }

    // Serialization helpers
    private class ProductFamilyWrapper { [JsonPropertyName("product_family")] public ProductFamily? ProductFamily { get; set; } }
    private class ProductFamily { public int Id { get; set; } public string Handle { get; set; } = ""; }
    private class ProductWrapper { [JsonPropertyName("product")] public ProductInfo? Product { get; set; } }
    private class ProductInfo { public int Id { get; set; } public string Handle { get; set; } = ""; public string Name { get; set; } = ""; [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; } [JsonPropertyName("interval_unit")] public string IntervalUnit { get; set; } = ""; public int Interval { get; set; } }
    private class CustomerResponse { [JsonPropertyName("customer")] public CustomerInfo? Customer { get; set; } }
    private class CustomerWrapper { [JsonPropertyName("customer")] public CustomerInfo? Customer { get; set; } }
    private class CustomerInfo { public int Id { get; set; } public string Email { get; set; } = ""; public string Reference { get; set; } = ""; }
    private class SubscriptionResponse { [JsonPropertyName("subscription")] public SubscriptionInfo? Subscription { get; set; } }
    private class SubscriptionWrapper { [JsonPropertyName("subscription")] public SubscriptionInfo? Subscription { get; set; } }
    private class SubscriptionInfo
    {
        public int Id { get; set; }
        [JsonPropertyName("product_handle")] public string? ProductHandle { get; set; }
        public string? State { get; set; }
        [JsonPropertyName("current_period_started_at")] public DateTime? CurrentPeriodStartedAt { get; set; }
        [JsonPropertyName("next_billing_at")] public DateTime? NextBillingAt { get; set; }
        [JsonPropertyName("customer_id")] public int? CustomerId { get; set; }
    }
}
