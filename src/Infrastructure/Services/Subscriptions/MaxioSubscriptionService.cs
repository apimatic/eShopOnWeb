using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Subscriptions;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IOptions<MaxioSettings> opts, IHttpClientFactory factory, ILogger<MaxioSubscriptionService> logger)
    {
        _settings = opts.Value;
        _logger = logger;
        _http = factory.CreateClient("Maxio");
        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl) ? _settings.BaseUrl : $"https://{_settings.Subdomain}.chargify.com/api/v1/";
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        // Confirmed endpoint pattern: Chargify v1 /api/v1/products.json (handle-based lookup also available)
        // Using family handle to scope if supported; otherwise falls back to direct product list attempt.
        var url = $"products.json?product_family_handle={_settings.ProductFamilyHandle}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadAsStringAsync();
                using var docRoot = JsonDocument.Parse(doc);
                var list = new List<SubscriptionPlanDto>();
                if (docRoot.RootElement.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in products.EnumerateArray())
                    {
                        list.Add(MapProduct(p));
                    }
                }
                else if (docRoot.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in docRoot.RootElement.EnumerateArray())
                    {
                        list.Add(MapProduct(p));
                    }
                }
                if (list.Count > 0) return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch plans from Maxio; using seed fallback.");
        }

        // Fallback based on confirmed sandbox handles (stable handles; numeric IDs not guaranteed)
        return new List<SubscriptionPlanDto>
        {
            new() { Handle = "eshop-pro", Name = "Pro Plan", PriceInCents = 29900, Period = "month", Unit = "month" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Period = "month", Unit = "month" }
        };
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userEmail)
    {
        // Idempotent lookup by customer reference / email
        var customer = await FindOrCreateCustomerAsync(userEmail);
        if (customer == null || !customer.Value.TryGetProperty("id", out var cid)) return new List<MySubscriptionDto>();

        // List subscriptions for customer; endpoint pattern /subscriptions.json?customer_id={id}
        try
        {
            var resp = await _http.GetAsync($"subscriptions.json?customer_id={cid.GetInt32()}");
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadAsStringAsync();
                using var root = JsonDocument.Parse(doc);
                var list = new List<MySubscriptionDto>();
                if (root.RootElement.TryGetProperty("subscriptions", out var subs) && subs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in subs.EnumerateArray())
                        list.Add(MapSubscription(s));
                }
                else if (root.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in root.RootElement.EnumerateArray())
                        list.Add(MapSubscription(s));
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list subscriptions.");
        }
        return new List<MySubscriptionDto>();
    }

    public async Task<MySubscriptionDto> SubscribeAsync(string userEmail, string planHandle)
    {
        var customer = await FindOrCreateCustomerAsync(userEmail);
        if (customer == null) throw new InvalidOperationException("Maxio customer cannot be created.");

        int customerId = customer.Value.GetProperty("id").GetInt32();

        // Create subscription via POST /subscriptions.json
        var payload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = planHandle,
                ["customer_reference"] = userEmail,
                ["state"] = "active"
            }
        };

        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("subscriptions.json", content);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            _logger.LogError("Maxio subscribe failed: {Code} {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Subscription creation failed: {resp.StatusCode}");
        }

        var doc = await resp.Content.ReadAsStringAsync();
        using var root = JsonDocument.Parse(doc);
        var subProp = root.RootElement.TryGetProperty("subscription", out var s) ? s : root.RootElement;
        return MapSubscription(subProp);
    }

    private async Task<JsonElement?> FindOrCreateCustomerAsync(string email)
    {
        // Search by email/reference using customers.json with query
        try
        {
            var search = await _http.GetAsync($"customers.json?email={Uri.EscapeDataString(email)}");
            if (search.IsSuccessStatusCode)
            {
                var doc = await search.Content.ReadAsStringAsync();
                using var root = JsonDocument.Parse(doc);
                if (root.RootElement.TryGetProperty("customers", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
                    return arr[0];
                if (root.RootElement.ValueKind == JsonValueKind.Array && root.RootElement.GetArrayLength() > 0)
                    return root.RootElement[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Customer search failed.");
        }

        // Create customer idempotently by reference = email
        var createPayload = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["email"] = email,
                ["first_name"] = "Shopper",
                ["last_name"] = "User",
                ["reference"] = email
            }
        };
        var content = new StringContent(createPayload.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            var resp = await _http.PostAsync("customers.json", content);
            if (resp.IsSuccessStatusCode)
            {
                var doc = await resp.Content.ReadAsStringAsync();
                using var root = JsonDocument.Parse(doc);
                if (root.RootElement.TryGetProperty("customer", out var c)) return c;
                if (root.RootElement.ValueKind == JsonValueKind.Object) return root.RootElement;
            }
            else
            {
                // If already exists, try fetching again
                var search2 = await _http.GetAsync($"customers.json?email={Uri.EscapeDataString(email)}");
                if (search2.IsSuccessStatusCode)
                {
                    var doc = await search2.Content.ReadAsStringAsync();
                    using var root = JsonDocument.Parse(doc);
                    if (root.RootElement.TryGetProperty("customers", out var arr) && arr.GetArrayLength() > 0) return arr[0];
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer creation failed.");
        }
        return null;
    }

    private static SubscriptionPlanDto MapProduct(JsonElement el)
    {
        return new SubscriptionPlanDto
        {
            Handle = GetString(el, "handle") ?? GetString(el, "product_handle") ?? GetString(el, "id")?.ToString(),
            Name = GetString(el, "name") ?? GetString(el, "product_family_name"),
            PriceInCents = GetInt(el, "unit_price_in_cents") ?? GetInt(el, "price_in_cents") ?? 0,
            Period = GetString(el, "period") ?? "month"
        };
    }

    private static MySubscriptionDto MapSubscription(JsonElement el)
    {
        return new MySubscriptionDto
        {
            Id = GetInt(el, "id") ?? GetInt(el, "subscription_id"),
            PlanHandle = GetString(el, "product_handle") ?? GetString(el, "plan_handle"),
            PlanName = GetString(el, "product_name") ?? GetString(el, "plan_name"),
            PriceInCents = GetInt(el, "unit_price_in_cents") ?? 0,
            State = GetString(el, "state") ?? GetString(el, "status") ?? "unknown",
            NextBillingDate = GetString(el, "next_billing_date") ?? GetString(el, "next_assessment_date") ?? GetString(el, "current_period_ends_at")
        };
    }

    private static string GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String) return p.GetString();
        return null;
    }

    private static int? GetInt(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var p))
        {
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var j)) return j;
        }
        return null;
    }
}
