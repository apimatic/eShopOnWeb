using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioSettings settings, ILogger<MaxioSubscriptionService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri(_settings.GetBaseUrl());
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:x"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        using var client = CreateClient();
        var url = $"/products.json?product_family_handle={_settings.ProductFamilyHandle}";
        _logger.LogInformation("Maxio GET {Url}", url);
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var plans = new List<SubscriptionPlanDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var p = item.GetProperty("product");
            var handle = p.GetProperty("handle").GetString() ?? "";
            var name = p.GetProperty("name").GetString() ?? "";
            var priceCents = p.GetProperty("price_in_cents").GetInt32();
            var interval = p.GetProperty("interval_unit").GetString() ?? "month";
            plans.Add(new SubscriptionPlanDto(handle, name, priceCents / 100m, interval));
        }
        return plans;
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference)
    {
        var customerId = await ResolveCustomerIdAsync(userReference);
        if (customerId == null)
            return new List<MySubscriptionDto>();

        using var client = CreateClient();
        var url = $"/subscriptions.json?customer_id={customerId}";
        _logger.LogInformation("Maxio GET {Url}", url);
        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var subs = new List<MySubscriptionDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var s = item.GetProperty("subscription");
            var state = s.GetProperty("state").GetString() ?? "unknown";
            var currentPeriodEnds = s.GetProperty("current_period_ends_at").GetString();
            var product = s.GetProperty("product");
            var handle = product.GetProperty("handle").GetString() ?? "";
            var name = product.GetProperty("name").GetString() ?? "";
            subs.Add(new MySubscriptionDto(handle, name, state, currentPeriodEnds ?? ""));
        }
        return subs;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string planHandle)
    {
        var customerId = await ResolveCustomerIdAsync(userReference);
        if (customerId == null)
        {
            customerId = await CreateCustomerAsync(userReference, email);
        }
        if (customerId == null)
            return new SubscriptionResultDto(false, "Failed to create/find Maxio customer.", null, null, null);

        // Idempotency: check existing active subscription for this product
        var existing = await FindActiveSubscriptionAsync(customerId.Value, planHandle);
        if (existing != null)
        {
            return new SubscriptionResultDto(true, "Subscription already active.", existing.PlanHandle, existing.State, existing.NextBillingDate);
        }

        using var client = CreateClient();
        var body = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = planHandle,
                ["customer_id"] = customerId,
                ["payment_collection_method"] = "remittance"
            }
        };
        var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        _logger.LogInformation("Maxio POST /subscriptions.json customer={CustomerId} plan={Plan}", customerId, planHandle);
        var resp = await client.PostAsync("/subscriptions.json", content);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("Maxio subscription creation failed: {Status} {Body}", resp.StatusCode, err);
            return new SubscriptionResultDto(false, $"Subscription creation failed: {resp.StatusCode}", null, null, null);
        }
        var respJson = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        var s = doc.RootElement.GetProperty("subscription");
        var subState = s.GetProperty("state").GetString() ?? "";
        var nextBilling = s.GetProperty("current_period_ends_at").GetString() ?? "";
        var prod = s.GetProperty("product");
        var subPlanHandle = prod.GetProperty("handle").GetString() ?? planHandle;
        return new SubscriptionResultDto(true, "Subscribed successfully.", subPlanHandle, subState, nextBilling);
    }

    private async Task<int?> ResolveCustomerIdAsync(string userReference)
    {
        using var client = CreateClient();
        var url = $"/customers/lookup.json?reference={Uri.EscapeDataString(userReference)}";
        try
        {
            var resp = await client.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("customer", out var cust))
                return cust.GetProperty("id").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Maxio customer lookup failed for reference {Ref}", userReference);
        }
        return null;
    }

    private async Task<int?> CreateCustomerAsync(string reference, string email)
    {
        using var client = CreateClient();
        var body = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["email"] = email,
                ["first_name"] = "eShop",
                ["last_name"] = "User",
                ["reference"] = reference
            }
        };
        var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            var resp = await client.PostAsync("/customers.json", content);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("customer", out var cust))
                return cust.GetProperty("id").GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maxio customer creation failed for reference {Ref}", reference);
        }
        return null;
    }

    private async Task<MySubscriptionDto?> FindActiveSubscriptionAsync(int customerId, string planHandle)
    {
        using var client = CreateClient();
        var url = $"/subscriptions.json?customer_id={customerId}";
        try
        {
            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var s = item.GetProperty("subscription");
                var state = s.GetProperty("state").GetString();
                if (state == "active" || state == "trialing")
                {
                    var product = s.GetProperty("product");
                    var handle = product.GetProperty("handle").GetString();
                    if (handle == planHandle)
                    {
                        var currentPeriodEnds = s.GetProperty("current_period_ends_at").GetString() ?? "";
                        var name = product.GetProperty("name").GetString() ?? planHandle;
                        return new MySubscriptionDto(handle, name, state, currentPeriodEnds);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Maxio subscription lookup failed for customer {Id}", customerId);
        }
        return null;
    }
}
