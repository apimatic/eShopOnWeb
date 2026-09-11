using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<PlanInfo>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct = default);
    Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class PlanInfo
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Interval { get; set; } = "month";
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public decimal PriceInCents { get; set; }
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:X"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string Base => _settings.GetBaseUrl();

    public async Task<List<PlanInfo>> GetPlansAsync(CancellationToken ct = default)
    {
        // Use product family handle to filter; endpoint verified by docs: /products
        // We fetch all products and filter by family handle if needed.
        var url = $"{Base}/api/v1/products.json";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<PlanInfo>();
        if (doc.RootElement.TryGetProperty("products", out var prods))
        {
            foreach (var p in prods.EnumerateArray())
            {
                var family = p.GetProperty("product_family").GetProperty("handle").GetString() ?? "";
                if (!string.IsNullOrEmpty(_settings.ProductFamilyHandle) && family != _settings.ProductFamilyHandle)
                    continue;
                list.Add(new PlanInfo
                {
                    Id = p.GetProperty("id").GetInt32(),
                    Handle = p.GetProperty("handle").GetString() ?? "",
                    Name = p.GetProperty("name").GetString() ?? "",
                    PriceInCents = (int)(p.GetProperty("price_in_cents").GetDouble()),
                    Interval = p.GetProperty("interval").GetString() ?? "month"
                });
            }
        }
        return list;
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer by reference = userId
        var cust = await EnsureCustomerAsync(userId, email, ct);
        // Idempotent subscribe: create subscription; if exists, return existing
        var url = $"{Base}/api/v1/subscriptions.json";
        var body = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = userId,
                customer_id = cust.Id,
                // No trial, no setup fee; payment method not required for sandbox
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(url, content, ct);
        // If conflict/duplicate, attempt to fetch existing
        if ((int)resp.StatusCode == 422 || (int)resp.StatusCode == 409)
        {
            var subs = await GetMySubscriptionsAsync(userId, ct);
            var existing = subs.FirstOrDefault(s => s.PlanHandle == planHandle);
            if (existing != null) return existing;
        }
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var sub = doc.RootElement.GetProperty("subscription");
        return MapSubscription(sub);
    }

    public async Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var url = $"{Base}/api/v1/subscriptions.json?customer_reference={Uri.EscapeDataString(userId)}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<SubscriptionInfo>();
        if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var s in subs.EnumerateArray())
                list.Add(MapSubscription(s));
        }
        return list;
    }

    private async Task<(int Id, string Reference)> EnsureCustomerAsync(string userId, string email, CancellationToken ct)
    {
        // Try find by reference
        var findUrl = $"{Base}/api/v1/customers.json?reference={Uri.EscapeDataString(userId)}";
        var resp = await _http.GetAsync(findUrl, ct);
        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("customers", out var custs) && custs.GetArrayLength() > 0)
            {
                var c = custs[0];
                return (c.GetProperty("id").GetInt32(), c.GetProperty("reference").GetString() ?? userId);
            }
        }
        // Create
        var createUrl = $"{Base}/api/v1/customers.json";
        var body = new { customer = new { reference = userId, email = email, first_name = "Shopper", last_name = "User", organization = "eShopOnWeb" } };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var createResp = await _http.PostAsync(createUrl, content, ct);
        createResp.EnsureSuccessStatusCode();
        var createJson = await createResp.Content.ReadAsStringAsync(ct);
        using var doc2 = JsonDocument.Parse(createJson);
        var created = doc2.RootElement.GetProperty("customer");
        return (created.GetProperty("id").GetInt32(), created.GetProperty("reference").GetString() ?? userId);
    }

    private SubscriptionInfo MapSubscription(JsonElement s)
    {
        var planHandle = s.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() ?? "" : "";
        if (string.IsNullOrEmpty(planHandle) && s.TryGetProperty("product_handle", out var ph))
            planHandle = ph.GetString() ?? "";
        s.TryGetProperty("next_billing_at", out var nextProp);
        var next = nextProp.ValueKind != JsonValueKind.Null ? nextProp.GetDateTime() : (DateTime?)null;
        s.TryGetProperty("product", out var prodEl);
        s.TryGetProperty("state", out var stateEl);
        string planName = "";
        if (prodEl.ValueKind != JsonValueKind.Undefined && prodEl.TryGetProperty("name", out var nEl))
            planName = nEl.GetString() ?? "";
        int priceInCents = 0;
        if (prodEl.ValueKind != JsonValueKind.Undefined && prodEl.TryGetProperty("price_in_cents", out var pcEl))
            priceInCents = (int)pcEl.GetDouble();
        return new SubscriptionInfo
        {
            Id = s.GetProperty("id").GetInt32(),
            PlanHandle = planHandle,
            PlanName = planName,
            State = stateEl.ValueKind != JsonValueKind.Undefined ? stateEl.GetString() ?? "" : "",
            NextBillingDate = next,
            PriceInCents = priceInCents
        };
    }
}
