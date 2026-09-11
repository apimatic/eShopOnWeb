using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioOptions _opts;

    public MaxioService(IOptions<MaxioOptions> opts, HttpClient http)
    {
        _opts = opts.Value;
        _http = http;
        _http.BaseAddress = new Uri(GetBaseUrl());
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_opts.ApiKey}:x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_opts.BaseUrl))
            return _opts.BaseUrl.TrimEnd('/');
        return $"https://{_opts.Subdomain}.chargify.com";
    }

    public async Task<List<PlanDto>> GetSubscriptionPlansAsync()
    {
        // Get family by handle
        var families = await _http.GetFromJsonAsync<JsonArray>("/product_families.json");
        int? familyId = null;
        if (families != null)
        {
            foreach (var f in families)
            {
                if (f is JsonObject obj && obj["product_family"] is JsonObject pf && pf["handle"]?.GetValue<string>() == _opts.ProductFamilyHandle)
                {
                    familyId = pf["id"]?.GetValue<int>();
                    break;
                }
            }
        }

        if (familyId == null)
        {
            // fallback: list all products and filter by family handle if response contains it
            var all = await _http.GetFromJsonAsync<JsonArray>("/products.json");
            return ParsePlans(all);
        }

        var products = await _http.GetFromJsonAsync<JsonArray>($"/product_families/{familyId}/products.json");
        return ParsePlans(products);
    }

    private static List<PlanDto> ParsePlans(JsonArray? arr)
    {
        var list = new List<PlanDto>();
        if (arr == null) return list;
        foreach (var item in arr)
        {
            if (item is not JsonObject obj) continue;
            var p = obj["product"] is JsonObject prod ? prod : obj;
            var handle = p["api_handle"]?.GetValue<string>() ?? p["handle"]?.GetValue<string>() ?? "";
            var name = p["name"]?.GetValue<string>() ?? "";
            // Try to get price from first price point or product price
            decimal price = 0;
            if (p["product_price_in_cents"] != null)
            {
                price = p["product_price_in_cents"]!.GetValue<int>() / 100m;
            }
            else if (p["price_in_cents"] != null)
            {
                price = p["price_in_cents"]!.GetValue<int>() / 100m;
            }
            list.Add(new PlanDto
            {
                Id = p["id"]?.GetValue<int>() ?? 0,
                Handle = handle,
                Name = name,
                Price = price,
                Currency = p["currency"]?.GetValue<string>() ?? "USD",
                FamilyHandle = p["family"] is JsonObject fam ? fam["handle"]?.GetValue<string>() ?? "" : ""
            });
        }
        return list;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, string customerReference, string firstName, string lastName, string email)
    {
        // Idempotent customer creation / lookup
        int customerId;
        try
        {
            var lookup = await _http.GetFromJsonAsync<JsonObject>($"/customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}");
            if (lookup != null && lookup["customer"] is JsonObject c)
            {
                customerId = c["id"]!.GetValue<int>();
            }
            else
            {
                throw new Exception("Customer lookup failed");
            }
        }
        catch
        {
            var createReq = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = customerReference
                }
            };
            var resp = await _http.PostAsJsonAsync("/customers.json", createReq);
            resp.EnsureSuccessStatusCode();
            var createBody = await resp.Content.ReadFromJsonAsync<JsonObject>();
            if (createBody == null || createBody["customer"] is not JsonObject c)
                throw new Exception("Failed to create Maxio customer");
            customerId = c["id"]!.GetValue<int>();
        }

        // Idempotent subscription: check by subscription reference = customerReference (or append)
        var subRef = customerReference; // can also use $"sub-{customerReference}" but keep simple
        try
        {
            var subLookup = await _http.GetFromJsonAsync<JsonObject>($"/subscriptions/lookup.json?reference={Uri.EscapeDataString(subRef)}");
            if (subLookup != null && subLookup["subscription"] is JsonObject s)
            {
                return MapSubscription(s);
            }
        }
        catch { /* not found */ }

        var subReq = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference,
                reference = subRef
            }
        };
        var subResp = await _http.PostAsJsonAsync("/subscriptions.json", subReq);
        subResp.EnsureSuccessStatusCode();
        var subBody = await subResp.Content.ReadFromJsonAsync<JsonObject>();
        if (subBody == null || subBody["subscription"] is not JsonObject subObj)
            throw new Exception("Failed to create Maxio subscription");
        return MapSubscription(subObj);
    }

    public async Task<List<SubscriptionDto>> GetMySubscriptionsAsync(string customerReference)
    {
        int customerId = 0;
        try
        {
            var lookup = await _http.GetFromJsonAsync<JsonObject>($"/customers/lookup.json?reference={Uri.EscapeDataString(customerReference)}");
            if (lookup != null && lookup["customer"] is JsonObject c)
                customerId = c["id"]!.GetValue<int>();
        }
        catch { return new List<SubscriptionDto>(); }

        if (customerId == 0) return new List<SubscriptionDto>();

        var subs = await _http.GetFromJsonAsync<JsonArray>($"/customers/{customerId}/subscriptions.json");
        var list = new List<SubscriptionDto>();
        if (subs == null) return list;
        foreach (var item in subs)
        {
            if (item is JsonObject obj && obj["subscription"] is JsonObject s)
                list.Add(MapSubscription(s));
        }
        return list;
    }

    private static SubscriptionDto MapSubscription(JsonObject s)
    {
        return new SubscriptionDto
        {
            Id = s["id"]?.GetValue<int>() ?? 0,
            State = s["state"]?.GetValue<string>() ?? "",
            ProductHandle = s["product_handle"]?.GetValue<string>() ?? (s["product"] is JsonObject p ? p["api_handle"]?.GetValue<string>() ?? p["handle"]?.GetValue<string>() : ""),
            ProductName = s["product"] is JsonObject pr ? pr["name"]?.GetValue<string>() ?? "" : (s["product_name"]?.GetValue<string>() ?? ""),
            Reference = s["reference"]?.GetValue<string>() ?? "",
            CurrentPeriodEndsAt = s["current_period_ends_at"]?.GetValue<string>() ?? "",
            NextBillingAt = s["next_billing_at"]?.GetValue<string>() ?? s["next_assessment_at"]?.GetValue<string>() ?? "",
            BalanceInCents = s["balance_in_cents"]?.GetValue<int>() ?? 0,
            ProductPriceInCents = s["product_price_in_cents"]?.GetValue<int>() ?? 0
        };
    }
}
