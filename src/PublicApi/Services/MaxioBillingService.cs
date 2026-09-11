using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string userId, string email, string name, string planHandle, CancellationToken ct = default);
    Task<List<MySubscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IOptions<MaxioSettings> settings, ILogger<MaxioBillingService> logger, IHttpClientFactory factory)
    {
        _settings = settings.Value;
        _logger = logger;
        _http = factory.CreateClient();
        var baseUrl = GetBaseUrl();
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ApiKey}:")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return _settings.BaseUrl.TrimEnd('/');
        return $"https://{_settings.Subdomain}.chargify.com";
    }

    public async Task<List<SubscriptionPlan>> GetPlansAsync(CancellationToken ct = default)
    {
        var url = $"/products.json?product_family_handle={_settings.ProductFamilyHandle}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var plans = new List<SubscriptionPlan>();
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item?["product"] is JsonObject p)
                {
                    plans.Add(new SubscriptionPlan
                    {
                        Id = p["id"]?.GetValue<int>() ?? 0,
                        Name = p["name"]?.GetValue<string>() ?? "",
                        Handle = p["handle"]?.GetValue<string>() ?? "",
                        PriceInCents = p["price_in_cents"]?.GetValue<int>() ?? 0,
                        Interval = p["interval"]?.GetValue<int>() ?? 0,
                        IntervalUnit = p["interval_unit"]?.GetValue<string>() ?? "",
                        Description = p["description"]?.GetValue<string>()
                    });
                }
            }
        }
        return plans;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userId, string email, string name, string planHandle, CancellationToken ct = default)
    {
        // Ensure customer exists (idempotent by reference = userId)
        var customerId = await GetOrCreateCustomerAsync(userId, email, name, ct);

        var payload = new
        {
            subscription = new
            {
                product_handle = planHandle,
                customer_reference = userId,
                payment_collection_method = "remittance"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/subscriptions.json", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogError("Maxio subscribe failed: {Status} {Body}", resp.StatusCode, err);
            throw new InvalidOperationException($"Subscription creation failed: {resp.StatusCode} - {err}");
        }

        var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var sub = node?["subscription"] as JsonObject;
        var result = new SubscriptionResult
        {
            Id = sub?["id"]?.GetValue<int>() ?? 0,
            State = sub?["state"]?.GetValue<string>() ?? "",
            PlanHandle = sub?["product"]?["handle"]?.GetValue<string>() ?? "",
            PlanName = sub?["product"]?["name"]?.GetValue<string>() ?? "",
            PriceInCents = sub?["product_price_in_cents"]?.GetValue<int>() ?? 0,
            NextBillingDate = sub?["current_period_ends_at"]?.GetValue<string>() ?? "",
            CustomerId = sub?["customer"]?["id"]?.GetValue<int>() ?? customerId
        };
        return result;
    }

    public async Task<List<MySubscription>> GetMySubscriptionsAsync(string userId, CancellationToken ct = default)
    {
        var customerId = await GetCustomerIdAsync(userId, ct);
        if (customerId == 0)
            return new List<MySubscription>();

        var url = $"/subscriptions.json?customer_id={customerId}";
        var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var subs = new List<MySubscription>();
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item?["subscription"] is JsonObject s)
                {
                    subs.Add(new MySubscription
                    {
                        Id = s["id"]?.GetValue<int>() ?? 0,
                        State = s["state"]?.GetValue<string>() ?? "",
                        PlanHandle = s["product"]?["handle"]?.GetValue<string>() ?? "",
                        PlanName = s["product"]?["name"]?.GetValue<string>() ?? "",
                        PriceInCents = s["product_price_in_cents"]?.GetValue<int>() ?? 0,
                        CurrentPeriodEndsAt = s["current_period_ends_at"]?.GetValue<string>() ?? "",
                        NextAssessmentAt = s["next_assessment_at"]?.GetValue<string>() ?? ""
                    });
                }
            }
        }
        return subs;
    }

    private async Task<int> GetOrCreateCustomerAsync(string userId, string email, string name, CancellationToken ct)
    {
        var existing = await GetCustomerIdAsync(userId, ct);
        if (existing > 0)
            return existing;

        var (first, last) = SplitName(name);
        if (string.IsNullOrWhiteSpace(last)) last = "User";
        var payload = new
        {
            customer = new
            {
                email = email,
                first_name = first,
                last_name = last,
                reference = userId
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync("/customers.json", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            // If duplicate reference error happens race-wise, try lookup again
            if (err.Contains("must be unique") || err.Contains("taken"))
            {
                existing = await GetCustomerIdAsync(userId, ct);
                if (existing > 0) return existing;
            }
            throw new InvalidOperationException($"Customer creation failed: {resp.StatusCode} - {err}");
        }
        var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return node?["customer"]?["id"]?.GetValue<int>() ?? 0;
    }

    private async Task<int> GetCustomerIdAsync(string userId, CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync($"/customers/lookup.json?reference={userId}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return 0;
            resp.EnsureSuccessStatusCode();
            var node = await JsonNode.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return node?["customer"]?["id"]?.GetValue<int>() ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup Maxio customer for reference {Ref}", userId);
            return 0;
        }
    }

    private static (string First, string Last) SplitName(string full)
    {
        if (string.IsNullOrWhiteSpace(full)) return ("", "");
        var parts = full.Split(' ', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
    }
}
