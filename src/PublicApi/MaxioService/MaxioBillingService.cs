using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.MaxioService;

public interface IMaxioBillingService
{
    Task<List<PlanDto>> GetPlansAsync();
    Task<SubscriptionResult> SubscribeAsync(string userReference, string planHandle);
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference);
}

public class PlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string PriceFormatted => $"${Price:F2}/mo";
}

public class SubscriptionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? NextBillingAt { get; set; }
    public string? PlanHandle { get; set; }
    public decimal? Price { get; set; }
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string NextBillingAt { get; set; } = string.Empty;
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(IOptions<MaxioSettings> options, IHttpClientFactory? factory = null)
    {
        _settings = options.Value;
        _httpClient = factory?.CreateClient("Maxio") ?? new HttpClient();
        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl
            : $"https://{_settings.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x")));
    }

    public async Task<List<PlanDto>> GetPlansAsync()
    {
        var url = $"/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
        var resp = await _httpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadAsStringAsync();
        // Parse minimal: extract products array from response
        using var json = JsonDocument.Parse(doc);
        var products = new List<PlanDto>();
        if (json.RootElement.TryGetProperty("products", out var prods))
        {
            foreach (var p in prods.EnumerateArray())
            {
                var id = p.GetProperty("id").GetInt32();
                var handle = p.GetProperty("api_handle").GetString() ?? "";
                var name = p.GetProperty("name").GetString() ?? "";
                // Price from first price point if available; fallback 0
                decimal price = 0;
                if (p.TryGetProperty("price_points", out var pts) && pts.GetArrayLength() > 0)
                {
                    var priceStr = pts[0].GetProperty("price_in_cents").GetInt32();
                    price = priceStr / 100m;
                }
                products.Add(new PlanDto { Id = id, Handle = handle, Name = name, Price = price });
            }
        }
        return products;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userReference, string planHandle)
    {
        // Idempotent customer ensure
        var customerResp = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
        if (customerResp.StatusCode == System.Net.HttpStatusCode.OK)
        {
            // exists; use reference
        }
        else
        {
            // create customer
            var createBody = new
            {
                customer = new
                {
                    first_name = userReference.Split('@').Length > 0 ? userReference.Split('@')[0] : userReference,
                    last_name = "User",
                    email = userReference,
                    reference = userReference
                }
            };
            var content = new StringContent(JsonSerializer.Serialize(createBody), Encoding.UTF8, "application/json");
            var createResp = await _httpClient.PostAsync("/customers.json", content);
            // If conflict/already exists, ignore
            if (!createResp.IsSuccessStatusCode && createResp.StatusCode != System.Net.HttpStatusCode.Created && createResp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // try lookup again
                customerResp = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
            }
        }

        // Create subscription using customer_reference
        var subBody = new
        {
            customer_reference = userReference,
            product_handle = planHandle
        };
        var subContent = new StringContent(JsonSerializer.Serialize(subBody), Encoding.UTF8, "application/json");
        var subResp = await _httpClient.PostAsync("/subscriptions.json", subContent);
        if (!subResp.IsSuccessStatusCode)
        {
            var err = await subResp.Content.ReadAsStringAsync();
            return new SubscriptionResult { Success = false, Error = err };
        }
        var subDoc = await subResp.Content.ReadAsStringAsync();
        using var subJson = JsonDocument.Parse(subDoc);
        int subId = 0;
        string state = "";
        string? nextBilling = null;
        if (subJson.RootElement.TryGetProperty("subscription", out var s))
        {
            if (s.TryGetProperty("id", out var sid)) subId = sid.GetInt32();
            if (s.TryGetProperty("state", out var st)) state = st.GetString() ?? "";
            if (s.TryGetProperty("next_billing_at", out var nb)) nextBilling = nb.GetString();
        }
        // Get product handle/price for response using plan handle
        return new SubscriptionResult
        {
            Success = true,
            SubscriptionId = subId,
            State = state,
            NextBillingAt = nextBilling,
            PlanHandle = planHandle
        };
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference)
    {
        // First find customer by reference
        var lookup = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
        if (lookup.StatusCode != System.Net.HttpStatusCode.OK) return new List<MySubscriptionDto>();
        var lookupDoc = await lookup.Content.ReadAsStringAsync();
        using var lookupJson = JsonDocument.Parse(lookupDoc);
        int customerId = 0;
        if (lookupJson.RootElement.TryGetProperty("customer", out var cust) && cust.TryGetProperty("id", out var cid))
            customerId = cid.GetInt32();

        if (customerId == 0) return new List<MySubscriptionDto>();

        var subsResp = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
        if (subsResp.StatusCode != System.Net.HttpStatusCode.OK) return new List<MySubscriptionDto>();
        var subsDoc = await subsResp.Content.ReadAsStringAsync();
        using var subsJson = JsonDocument.Parse(subsDoc);
        var results = new List<MySubscriptionDto>();
        if (subsJson.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in subsJson.RootElement.EnumerateArray())
            {
                var id = s.GetProperty("id").GetInt32();
                var state = s.GetProperty("state").GetString() ?? "";
                var handle = s.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? "" : "";
                var name = s.TryGetProperty("product_name", out var pn) ? pn.GetString() ?? "" : handle;
                decimal price = 0;
                if (s.TryGetProperty("product_price_in_cents", out var ppc)) price = ppc.GetInt32() / 100m;
                else if (s.TryGetProperty("monthly_price_in_cents", out var mpc)) price = mpc.GetInt32() / 100m;
                var next = s.TryGetProperty("next_billing_at", out var nb) ? nb.GetString() ?? "" : "";
                results.Add(new MySubscriptionDto { Id = id, State = state, ProductHandle = handle, ProductName = name, Price = price, NextBillingAt = next });
            }
        }
        return results;
    }
}
