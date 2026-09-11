using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi;

public interface IMaxioService
{
    Task<List<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string reference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<List<MySubscription>> GetMySubscriptionsAsync(string reference, CancellationToken ct = default);
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionResult
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime NextBillingAt { get; set; }
}

public class MySubscription
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime NextBillingAt { get; set; }
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _client;
    private readonly MaxioSettings _settings;

    public MaxioService(IOptions<MaxioSettings> options, IHttpClientFactory? factory = null)
    {
        _settings = options.Value;
        _client = factory?.CreateClient("Maxio") ?? new HttpClient();
        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl) ? _settings.BaseUrl : $"https://{_settings.Subdomain}.chargify.com";
        _client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        _client.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default)
    {
        // Find product family by handle
        var familyResp = await _client.GetAsync("/product_families.json", ct);
        familyResp.EnsureSuccessStatusCode();
        var familyJson = await familyResp.Content.ReadAsStringAsync(ct);
        var familyDoc = JsonDocument.Parse(familyJson);
        int familyId = 0;
        foreach (var item in familyDoc.RootElement.EnumerateArray())
        {
            var fam = item.GetProperty("product_family");
            if (fam.GetProperty("handle").GetString() == _settings.ProductFamilyHandle)
            {
                familyId = fam.GetProperty("id").GetInt32();
                break;
            }
        }

        if (familyId == 0) return new List<SubscriptionPlan>();

        var prodResp = await _client.GetAsync($"/product_families/{familyId}/products.json", ct);
        prodResp.EnsureSuccessStatusCode();
        var prodJson = await prodResp.Content.ReadAsStringAsync(ct);
        var prodDoc = JsonDocument.Parse(prodJson);
        var plans = new List<SubscriptionPlan>();
        foreach (var item in prodDoc.RootElement.EnumerateArray())
        {
            var prod = item.GetProperty("product");
            plans.Add(new SubscriptionPlan
            {
                Id = prod.GetProperty("id").GetInt32(),
                Handle = prod.GetProperty("handle").GetString() ?? string.Empty,
                Name = prod.GetProperty("name").GetString() ?? string.Empty,
                Description = prod.TryGetProperty("description", out var d) ? d.GetString() ?? string.Empty : string.Empty,
                Price = prod.TryGetProperty("price_in_cents", out var p) ? p.GetInt32() / 100.0m : 0,
                Interval = prod.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 0,
                IntervalUnit = prod.TryGetProperty("interval_unit", out var iu) ? iu.GetString() ?? string.Empty : string.Empty,
            });
        }
        return plans;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string reference, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default)
    {
        // Idempotent customer lookup by reference
        int customerId = 0;
        try
        {
            var lookupResp = await _client.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
            if (lookupResp.IsSuccessStatusCode)
            {
                var lookupJson = await lookupResp.Content.ReadAsStringAsync(ct);
                var lookupDoc = JsonDocument.Parse(lookupJson);
                if (lookupDoc.RootElement.TryGetProperty("customer", out var cust))
                {
                    customerId = cust.GetProperty("id").GetInt32();
                }
            }
        }
        catch { /* ignore */ }

        if (customerId == 0)
        {
            var createPayload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = reference
                }
            };
            var json = JsonSerializer.Serialize(createPayload);
            var resp = await _client.PostAsync("/customers.json", new StringContent(json, Encoding.UTF8, "application/json"), ct);
            resp.EnsureSuccessStatusCode();
            var respJson = await resp.Content.ReadAsStringAsync(ct);
            var respDoc = JsonDocument.Parse(respJson);
            if (respDoc.RootElement.TryGetProperty("customer", out var c))
            {
                customerId = c.GetProperty("id").GetInt32();
            }
        }

        // Check for existing subscription by reference (optional idempotency)
        // We use a reference derived from user reference + product handle
        string subRef = $"eshop-sub-{reference}-{productHandle}";
        int existingSubId = 0;
        try
        {
            var subLookupResp = await _client.GetAsync($"/subscriptions/lookup.json?reference={Uri.EscapeDataString(subRef)}", ct);
            if (subLookupResp.IsSuccessStatusCode)
            {
                var subJson = await subLookupResp.Content.ReadAsStringAsync(ct);
                var subDoc = JsonDocument.Parse(subJson);
                if (subDoc.RootElement.TryGetProperty("subscription", out var s))
                {
                    existingSubId = s.GetProperty("id").GetInt32();
                }
            }
        }
        catch { /* ignore */ }

        if (existingSubId > 0)
        {
            // Return existing
            var subResp = await _client.GetAsync($"/subscriptions/{existingSubId}.json", ct);
            subResp.EnsureSuccessStatusCode();
            var subJson = await subResp.Content.ReadAsStringAsync(ct);
            var subDoc = JsonDocument.Parse(subJson);
            if (subDoc.RootElement.TryGetProperty("subscription", out var sub))
            {
                return new SubscriptionResult
                {
                    SubscriptionId = sub.GetProperty("id").GetInt32(),
                    ProductHandle = sub.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? string.Empty : string.Empty,
                    Price = sub.TryGetProperty("product_price_in_cents", out var ppc) ? ppc.GetInt32() / 100.0m : 0,
                    State = sub.GetProperty("state").GetString() ?? string.Empty,
                    NextBillingAt = sub.TryGetProperty("next_billing_at", out var nb) && !string.IsNullOrEmpty(nb.GetString()) ? DateTime.Parse(nb.GetString()!) : DateTime.UtcNow
                };
            }
        }

        var createSubPayload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId,
                reference = subRef
            }
        };
        var subJsonPayload = JsonSerializer.Serialize(createSubPayload);
        var subRespCreate = await _client.PostAsync("/subscriptions.json", new StringContent(subJsonPayload, Encoding.UTF8, "application/json"), ct);
        subRespCreate.EnsureSuccessStatusCode();
        var subRespJson = await subRespCreate.Content.ReadAsStringAsync(ct);
        var subRespDoc = JsonDocument.Parse(subRespJson);
        if (subRespDoc.RootElement.TryGetProperty("subscription", out var newSub))
        {
            return new SubscriptionResult
            {
                SubscriptionId = newSub.GetProperty("id").GetInt32(),
                ProductHandle = newSub.TryGetProperty("product_handle", out var np) ? np.GetString() ?? string.Empty : string.Empty,
                Price = newSub.TryGetProperty("product_price_in_cents", out var npc) ? npc.GetInt32() / 100.0m : 0,
                State = newSub.GetProperty("state").GetString() ?? string.Empty,
                NextBillingAt = newSub.TryGetProperty("next_billing_at", out var nb2) && !string.IsNullOrEmpty(nb2.GetString()) ? DateTime.Parse(nb2.GetString()!) : DateTime.UtcNow
            };
        }
        throw new InvalidOperationException("Subscription creation did not return a subscription.");
    }

    public async Task<List<MySubscription>> GetMySubscriptionsAsync(string reference, CancellationToken ct = default)
    {
        // Lookup customer by reference to get id, then list subscriptions for customer
        int customerId = 0;
        try
        {
            var lookupResp = await _client.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}", ct);
            if (lookupResp.IsSuccessStatusCode)
            {
                var lookupJson = await lookupResp.Content.ReadAsStringAsync(ct);
                var lookupDoc = JsonDocument.Parse(lookupJson);
                if (lookupDoc.RootElement.TryGetProperty("customer", out var cust))
                {
                    customerId = cust.GetProperty("id").GetInt32();
                }
            }
        }
        catch { /* ignore */ }

        if (customerId == 0) return new List<MySubscription>();

        var subResp = await _client.GetAsync($"/customers/{customerId}/subscriptions.json", ct);
        subResp.EnsureSuccessStatusCode();
        var subJson = await subResp.Content.ReadAsStringAsync(ct);
        var subDoc = JsonDocument.Parse(subJson);
        var results = new List<MySubscription>();
        if (subDoc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var item in subs.EnumerateArray())
            {
                results.Add(new MySubscription
                {
                    Id = item.GetProperty("id").GetInt32(),
                    ProductHandle = item.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? string.Empty : string.Empty,
                    State = item.GetProperty("state").GetString() ?? string.Empty,
                    Price = item.TryGetProperty("product_price_in_cents", out var ppc) ? ppc.GetInt32() / 100.0m : 0,
                    NextBillingAt = item.TryGetProperty("next_billing_at", out var nb) && !string.IsNullOrEmpty(nb.GetString()) ? DateTime.Parse(nb.GetString()!) : DateTime.UtcNow
                });
            }
        }
        return results;
    }
}
