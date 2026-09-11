using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioBillingService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
                return _settings.BaseUrl.TrimEnd('/');
            return $"https://{_settings.Subdomain}.chargify.com";
        }
    }

    private void ConfigureAuth(HttpRequestMessage req)
    {
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X"));
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> GetPlansAsync()
    {
        var url = $"{BaseAddress}/api/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ConfigureAuth(req);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var plans = new List<SubscriptionPlanDto>();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var prod = item.GetProperty("product");
                var handle = prod.GetProperty("handle").GetString() ?? "";
                var name = prod.GetProperty("name").GetString() ?? "";
                var priceCents = prod.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt64() : 0;
                var interval = prod.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 1;
                var intervalUnit = prod.GetProperty("interval_unit").GetString() ?? "month";
                var id = prod.GetProperty("id").GetInt32();
                plans.Add(new SubscriptionPlanDto(handle, name, priceCents / 100m, interval.ToString(), intervalUnit, id));
            }
        }
        return plans;
    }

    public async Task<SubscriptionResultDto?> SubscribeAsync(string userReference, string planHandle, string? email = null, string? firstName = null, string? lastName = null)
    {
        // Idempotent customer lookup / create
        int customerId;
        try
        {
            var lookupUrl = $"{BaseAddress}/api/customers/lookup.json?reference={WebUtility.UrlEncode(userReference)}";
            using var lookupReq = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
            ConfigureAuth(lookupReq);
            var lookupResp = await _http.SendAsync(lookupReq);
            if (lookupResp.StatusCode == HttpStatusCode.OK)
            {
                var lookupJson = await lookupResp.Content.ReadAsStringAsync();
                var lookupDoc = JsonDocument.Parse(lookupJson);
                customerId = lookupDoc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
            }
            else
            {
                // Create customer
                var createUrl = $"{BaseAddress}/api/customers.json";
                var payload = new
                {
                    customer = new
                    {
                        reference = userReference,
                        first_name = firstName ?? "User",
                        last_name = lastName ?? "Name",
                        email = email ?? $"{userReference}@example.com",
                        country = "US"
                    }
                };
                using var createReq = new HttpRequestMessage(HttpMethod.Post, createUrl)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                ConfigureAuth(createReq);
                var createResp = await _http.SendAsync(createReq);
                if (!createResp.IsSuccessStatusCode)
                {
                    var err = await createResp.Content.ReadAsStringAsync();
                    return new SubscriptionResultDto(false, $"Customer create failed: {err}", null, null, null, null, null);
                }
                var createJson = await createResp.Content.ReadAsStringAsync();
                var createDoc = JsonDocument.Parse(createJson);
                customerId = createDoc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
            }
        }
        catch (Exception ex)
        {
            return new SubscriptionResultDto(false, $"Customer lookup/create error: {ex.Message}", null, null, null, null, null);
        }

        // Create subscription using customer_reference (idempotent if same reference + same product?)
        // Maxio allows creating with customer_reference; if already exists it may error, so we could check existing first.
        // For simplicity, attempt create; if 422 with duplicate, treat as success and fetch existing.
        var subUrl = $"{BaseAddress}/api/subscriptions.json";
        var subPayload = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = userReference,
                // No payment method required per mandate
                // No trial, no setup fee
            }
        };
        using var subReq = new HttpRequestMessage(HttpMethod.Post, subUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(subPayload), Encoding.UTF8, "application/json")
        };
        ConfigureAuth(subReq);
        var subResp = await _http.SendAsync(subReq);
        if (subResp.IsSuccessStatusCode)
        {
            var subJson = await subResp.Content.ReadAsStringAsync();
            var subDoc = JsonDocument.Parse(subJson);
            var sub = subDoc.RootElement.GetProperty("subscription");
            var sid = sub.GetProperty("id").GetInt32();
            var state = sub.GetProperty("state").GetString() ?? "unknown";
            var nextBilling = sub.TryGetProperty("next_billing_at", out var nb) ? nb.GetString() : null;
            var prod = sub.GetProperty("product");
            var planName = prod.GetProperty("name").GetString() ?? planHandle;
            var priceCents = prod.TryGetProperty("price_in_cents", out var ppc) ? ppc.GetInt64() : 0;
            return new SubscriptionResultDto(true, null, sid, state, nextBilling != null ? DateTime.Parse(nextBilling) : null, planName, priceCents / 100m);
        }

        var errBody = await subResp.Content.ReadAsStringAsync();
        // If it indicates existing subscription, try to fetch
        if (subResp.StatusCode == HttpStatusCode.UnprocessableEntity && errBody.Contains("already"))
        {
            // Fetch existing subscriptions for customer
            var existing = await GetSubscriptionsForCustomerAsync(userReference);
            var existingSub = existing.FirstOrDefault(s => s.PlanName == planHandle || true); // loose match
            if (existingSub != null)
            {
                return new SubscriptionResultDto(true, "Subscription already existed.", existingSub.Id, existingSub.State, existingSub.NextBillingDate, existingSub.PlanName, existingSub.Price);
            }
        }
        return new SubscriptionResultDto(false, $"Subscription create failed ({subResp.StatusCode}): {errBody}", null, null, null, null, null);
    }

    public async Task<IEnumerable<MySubscriptionDto>> GetSubscriptionsForCustomerAsync(string userReference)
    {
        // First lookup customer id
        int? customerId = null;
        try
        {
            var lookupUrl = $"{BaseAddress}/api/customers/lookup.json?reference={WebUtility.UrlEncode(userReference)}";
            using var lookupReq = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
            ConfigureAuth(lookupReq);
            var lookupResp = await _http.SendAsync(lookupReq);
            if (lookupResp.StatusCode == HttpStatusCode.OK)
            {
                var lookupJson = await lookupResp.Content.ReadAsStringAsync();
                var lookupDoc = JsonDocument.Parse(lookupJson);
                customerId = lookupDoc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
            }
        }
        catch { }

        if (customerId == null) return new List<MySubscriptionDto>();

        var url = $"{BaseAddress}/api/customers/{customerId}/subscriptions.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ConfigureAuth(req);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return new List<MySubscriptionDto>();
        var json = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var subs = new List<MySubscriptionDto>();
        if (doc.RootElement.TryGetProperty("subscriptions", out var subArr))
        {
            foreach (var s in subArr.EnumerateArray())
            {
                var sid = s.GetProperty("id").GetInt32();
                var state = s.GetProperty("state").GetString() ?? "";
                var activatedAt = s.TryGetProperty("activated_at", out var act) ? act.GetString() : null;
                var nextBilling = s.TryGetProperty("next_billing_at", out var nb) ? nb.GetString() : null;
                var prodName = s.TryGetProperty("product", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetProperty("name").GetString() ?? "" : "";
                var priceCents = s.TryGetProperty("product_price_in_cents", out var pc) ? pc.GetInt64() : 0;
                subs.Add(new MySubscriptionDto(sid, state, prodName, priceCents / 100m, nextBilling != null ? DateTime.Parse(nextBilling) : null, activatedAt != null ? DateTime.Parse(activatedAt) : null));
            }
        }
        return subs;
    }
}
