using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _productFamilyHandle;

    public MaxioBillingService(IConfiguration config, HttpClient httpClient)
    {
        var maxioConfig = config.GetSection("Maxio");
        var apiKey = maxioConfig["ApiKey"] ?? throw new InvalidOperationException("Maxio:ApiKey not set");
        var subdomain = maxioConfig["Subdomain"] ?? "cp-exp-1";
        _productFamilyHandle = maxioConfig["ProductFamilyHandle"] ?? "eshop-subscribe";
        var baseUrl = maxioConfig["BaseUrl"];
        _baseUrl = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : $"https://{subdomain}.chargify.com";

        _http = httpClient;
        _http.BaseAddress = new Uri(_baseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{apiKey}:x")));
    }

    public async Task<List<SubscriptionPlanInfo>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        // Direct REST: list products for family handle, then price points
        var result = new List<SubscriptionPlanInfo>();
        try
        {
            var resp = await _http.GetAsync($"/products.json?per_page=50", ct);
            resp.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("products", out var products))
            {
                foreach (var p in products.EnumerateArray())
                {
                    var handle = p.GetProperty("api_handle").GetString();
                    var nameProp = p.GetProperty("name"); var name = nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() : handle;
                    var id = p.GetProperty("id").GetInt32();
                    result.Add(new SubscriptionPlanInfo
                    {
                        Handle = handle,
                        Name = name,
                        Price = 0m,
                        Currency = "USD",
                        ProductFamilyId = id
                    });
                }
            }
        }
        catch { }
        return result;
    }

    public async Task<SubscriptionInfo> SubscribeAsync(string userReference, string planHandle, CancellationToken ct = default)
    {
        // Idempotent customer lookup by reference
        int customerId = 0;
        try
        {
            var r = await _http.GetAsync($"/customers/lookup.json?reference={userReference}", ct);
            if (r.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("customer", out var c))
                    customerId = c.GetProperty("id").GetInt32();
            }
        }
        catch { }

        if (customerId == 0)
        {
            var payload = new JsonObject
            {
                ["customer"] = new JsonObject
                {
                    ["email"] = userReference,
                    ["reference"] = userReference,
                    ["first_name"] = "Shopper",
                    ["last_name"] = "User"
                }
            };
            var r = await _http.PostAsJsonAsync("/customers.json", payload, ct);
            r.EnsureSuccessStatusCode();
            using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("customer", out var c))
                customerId = c.GetProperty("id").GetInt32();
        }

        var subPayload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = planHandle,
                ["customer_reference"] = userReference
            }
        };
        var subResp = await _http.PostAsJsonAsync("/subscriptions.json", subPayload, ct);
        subResp.EnsureSuccessStatusCode();
        using var subDoc = await JsonDocument.ParseAsync(await subResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var sub = subDoc.RootElement.GetProperty("subscription");
        return new SubscriptionInfo
        {
            Id = sub.GetProperty("id").GetInt32(),
                PlanHandle = sub.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? planHandle : planHandle,
            State = sub.TryGetProperty("state", out var st) ? st.GetString() : "unknown" ?? "unknown",
            NextAssessmentAt = sub.TryGetProperty("next_assessment_at", out var na) ? DateTimeOffset.Parse(na.GetString()) : (DateTimeOffset?)null,
            CurrentPeriodEndsAt = sub.TryGetProperty("current_period_ends_at", out var ce) ? DateTimeOffset.Parse(ce.GetString()) : (DateTimeOffset?)null,
            CreatedAt = sub.TryGetProperty("created_at", out var ca) ? DateTimeOffset.Parse(ca.GetString()) : (DateTimeOffset?)null
        };
    }

    public async Task<List<SubscriptionInfo>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var result = new List<SubscriptionInfo>();
        try
        {
            int customerId = 0;
            var r = await _http.GetAsync($"/customers/lookup.json?reference={userReference}", ct);
            if (r.IsSuccessStatusCode)
            {
                using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("customer", out var c))
                    customerId = c.GetProperty("id").GetInt32();
            }
            if (customerId > 0)
            {
                var sr = await _http.GetAsync($"/customers/{customerId}/subscriptions.json", ct);
                if (sr.IsSuccessStatusCode)
                {
                    using var sdoc = await JsonDocument.ParseAsync(await sr.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (sdoc.RootElement.TryGetProperty("subscriptions", out var arr))
                    {
                        foreach (var s in arr.EnumerateArray())
                        {
                            result.Add(new SubscriptionInfo
                            {
                                Id = s.GetProperty("id").GetInt32(),
                                PlanHandle = s.TryGetProperty("product_handle", out var ph2) ? ph2.GetString() ?? "" : "",
                                State = s.TryGetProperty("state", out var st2) ? st2.GetString() : "unknown" ?? "unknown",
                                NextAssessmentAt = s.TryGetProperty("next_assessment_at", out var na) ? DateTimeOffset.Parse(na.GetString()) : (DateTimeOffset?)null,
                                CurrentPeriodEndsAt = s.TryGetProperty("current_period_ends_at", out var ce) ? DateTimeOffset.Parse(ce.GetString()) : (DateTimeOffset?)null,
                                CreatedAt = s.TryGetProperty("created_at", out var ca) ? DateTimeOffset.Parse(ca.GetString()) : (DateTimeOffset?)null
                            });
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }
}
