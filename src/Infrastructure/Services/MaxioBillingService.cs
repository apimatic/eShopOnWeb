using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(HttpClient http, IConfiguration config, ILogger<MaxioBillingService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        var apiKey = config["Maxio:ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey missing");
        var baseUrl = config["Maxio:BaseUrl"];
        if (string.IsNullOrEmpty(baseUrl))
        {
            var sub = config["Maxio:Subdomain"] ?? throw new InvalidOperationException("Maxio:Subdomain missing");
            var env = config["Maxio:Environment"] ?? "US";
            baseUrl = env == "EU" ? $"https://{sub}.ebilling.maxio.com" : $"https://{sub}.chargify.com";
        }
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        var familyHandle = _config["Maxio:ProductFamilyHandle"] ?? "eshop-subscribe";
        // List families to resolve numeric id by handle
        var familiesResp = await _http.GetAsync("product_families.json");
        familiesResp.EnsureSuccessStatusCode();
        var familiesDoc = await JsonDocument.ParseAsync(await familiesResp.Content.ReadAsStreamAsync());
        int familyId = 0;
        foreach (var fam in familiesDoc.RootElement.GetProperty("product_families").EnumerateArray())
        {
            if (fam.GetProperty("handle").GetString() == familyHandle)
            {
                familyId = fam.GetProperty("id").GetInt32();
                break;
            }
        }
        if (familyId == 0) throw new InvalidOperationException("Product family not found: " + familyHandle);
        var productsResp = await _http.GetAsync($"product_families/{familyId}/products.json");
        productsResp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await productsResp.Content.ReadAsStreamAsync());
        var plans = new List<SubscriptionPlanDto>();
        foreach (var p in doc.RootElement.GetProperty("products").EnumerateArray())
        {
            plans.Add(new SubscriptionPlanDto
            {
                Id = p.GetProperty("id").GetInt32(),
                Handle = p.GetProperty("handle").GetString() ?? "",
                Name = p.GetProperty("name").GetString() ?? "",
                Price = p.TryGetProperty("price_in_cents", out var c) ? c.GetInt32() / 100.0m : 0,
                IntervalUnit = p.TryGetProperty("interval_unit", out var u) ? u.GetString() ?? "" : ""
            });
        }
        return plans;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string productHandle)
    {
        // Idempotent customer lookup by reference
        int customerId = 0;
        var lookupResp = await _http.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
        if (lookupResp.IsSuccessStatusCode)
        {
            var lookupDoc = await JsonDocument.ParseAsync(await lookupResp.Content.ReadAsStreamAsync());
            if (lookupDoc.RootElement.TryGetProperty("customer", out var cust))
            {
                customerId = cust.GetProperty("id").GetInt32();
            }
        }
        if (customerId == 0)
        {
            var createCust = new { customer = new { reference = userReference, email = email, first_name = userReference, last_name = "" } };
            var json = JsonSerializer.Serialize(createCust);
            var resp = await _http.PostAsync("customers.json", new StringContent(json, Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            customerId = doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
        }
        // Check existing subscriptions for this customer
        var subsResp = await _http.GetAsync($"customers/{customerId}/subscriptions.json");
        if (subsResp.IsSuccessStatusCode)
        {
            var subsDoc = await JsonDocument.ParseAsync(await subsResp.Content.ReadAsStreamAsync());
            foreach (var s in subsDoc.RootElement.EnumerateArray())
            {
                var prodHandle = s.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() : "";
                if (prodHandle == productHandle)
                {
                    return new SubscriptionResultDto
                    {
                        Created = false,
                        SubscriptionId = s.GetProperty("id").GetInt32(),
                        State = s.GetProperty("state").GetString() ?? "",
                        Price = s.TryGetProperty("product_price_in_cents", out var pp) ? pp.GetInt32() / 100.0m : 0,
                        NextBillingDate = s.TryGetProperty("next_assessment_at", out var nb) ? nb.GetString() ?? "" : "",
                        ProductName = prod.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : ""
                    };
                }
            }
        }
        var payload = new { subscription = new { product_handle = productHandle, customer_reference = userReference } };
        var payloadJson = JsonSerializer.Serialize(payload);
        var subResp = await _http.PostAsync("subscriptions.json", new StringContent(payloadJson, Encoding.UTF8, "application/json"));
        subResp.EnsureSuccessStatusCode();
        var subDoc = await JsonDocument.ParseAsync(await subResp.Content.ReadAsStreamAsync());
        var sub = subDoc.RootElement.GetProperty("subscription");
        return new SubscriptionResultDto
        {
            Created = true,
            SubscriptionId = sub.GetProperty("id").GetInt32(),
            State = sub.GetProperty("state").GetString() ?? "",
            Price = sub.TryGetProperty("product_price_in_cents", out var p) ? p.GetInt32() / 100.0m : 0,
            NextBillingDate = sub.TryGetProperty("next_assessment_at", out var n) ? n.GetString() ?? "" : "",
            ProductName = sub.TryGetProperty("product", out var prodEl) ? prodEl.GetProperty("name").GetString() ?? "" : ""
        };
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference)
    {
        int customerId = 0;
        var lookupResp = await _http.GetAsync($"customers/lookup.json?reference={Uri.EscapeDataString(userReference)}");
        if (lookupResp.IsSuccessStatusCode)
        {
            var lookupDoc = await JsonDocument.ParseAsync(await lookupResp.Content.ReadAsStreamAsync());
            if (lookupDoc.RootElement.TryGetProperty("customer", out var cust))
            {
                customerId = cust.GetProperty("id").GetInt32();
            }
        }
        if (customerId == 0) return new List<MySubscriptionDto>();
        var subsResp = await _http.GetAsync($"customers/{customerId}/subscriptions.json");
        if (!subsResp.IsSuccessStatusCode) return new List<MySubscriptionDto>();
        var doc = await JsonDocument.ParseAsync(await subsResp.Content.ReadAsStreamAsync());
        var list = new List<MySubscriptionDto>();
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            list.Add(new MySubscriptionDto
            {
                SubscriptionId = s.GetProperty("id").GetInt32(),
                ProductName = s.TryGetProperty("product", out var prod) ? prod.GetProperty("name").GetString() ?? "" : "",
                State = s.GetProperty("state").GetString() ?? "",
                Price = s.TryGetProperty("product_price_in_cents", out var pp) ? pp.GetInt32() / 100.0m : 0,
                NextBillingDate = s.TryGetProperty("next_assessment_at", out var nb) ? nb.GetString() ?? "" : ""
            });
        }
        return list;
    }
}
