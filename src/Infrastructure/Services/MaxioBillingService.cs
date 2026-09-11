using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync();
    Task<MaxioCustomerDto> FindOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<SubscriptionDto?> FindSubscriptionAsync(string userId, string productHandle);
    Task<SubscriptionDto> CreateSubscriptionAsync(string customerReferenceOrId, string productHandle);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(string customerReferenceOrId);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IConfiguration config, ILogger<MaxioBillingService> logger)
    {
        _config = config;
        _logger = logger;
        _http = new HttpClient();
    }

    private string BaseUrl
    {
        get
        {
            var overrideUrl = _config["Maxio:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(overrideUrl)) return overrideUrl.TrimEnd('/');
            var sub = _config["Maxio:Subdomain"];
            return $"https://{sub}.chargify.com";
        }
    }

    private void ConfigureAuth()
    {
        var apiKey = _config["Maxio:ApiKey"];
        var byteArray = Encoding.ASCII.GetBytes($"{apiKey}:X");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync()
    {
        ConfigureAuth();
        var plans = new List<SubscriptionPlanDto>();
        var handles = new[] { "eshop-pro", "basic-plan" };
        foreach (var h in handles)
        {
            try
            {
                var resp = await _http.GetAsync($"{BaseUrl}/products/handle/{h}.json");
                if (!resp.IsSuccessStatusCode) continue;
                var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
                var prod = doc.RootElement.GetProperty("product");
                plans.Add(new SubscriptionPlanDto
                {
                    Handle = prod.GetProperty("handle").GetString() ?? h,
                    Name = prod.GetProperty("name").GetString() ?? h,
                    PriceInCents = prod.GetProperty("price_in_cents").GetInt64(),
                    Interval = prod.GetProperty("interval").GetInt32(),
                    IntervalUnit = prod.GetProperty("interval_unit").GetString() ?? "month",
                    Description = prod.TryGetProperty("description", out var d) ? d.GetString() : null,
                    ProductId = prod.GetProperty("id").GetInt32()
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plan handle {Handle}", h);
            }
        }
        return plans;
    }

    public async Task<MaxioCustomerDto> FindOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        ConfigureAuth();
        // Try find by reference = userId
        try
        {
            var findResp = await _http.GetAsync($"{BaseUrl}/customers/read_by_reference.json?reference={Uri.EscapeDataString(userId)}");
            if (findResp.IsSuccessStatusCode)
            {
                var doc = await JsonDocument.ParseAsync(await findResp.Content.ReadAsStreamAsync());
                if (doc.RootElement.TryGetProperty("customer", out var c))
                {
                    return new MaxioCustomerDto
                    {
                        Id = c.GetProperty("id").GetInt32(),
                        Reference = c.GetProperty("reference").GetString(),
                        Email = c.GetProperty("email").GetString()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Find customer by reference failed");
        }

        // Create
        var payload = new
        {
            customer = new
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                reference = userId
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{BaseUrl}/customers.json", content);
        resp.EnsureSuccessStatusCode();
        var doc2 = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var cust = doc2.RootElement.GetProperty("customer");
        return new MaxioCustomerDto
        {
            Id = cust.GetProperty("id").GetInt32(),
            Reference = cust.GetProperty("reference").GetString(),
            Email = cust.GetProperty("email").GetString()
        };
    }

    public async Task<SubscriptionDto?> FindSubscriptionAsync(string userId, string productHandle)
    {
        ConfigureAuth();
        // Find customer by reference first
        var customer = await FindOrCreateCustomerAsync(userId, "", "", "");
        var resp = await _http.GetAsync($"{BaseUrl}/customers/{customer.Id}/subscriptions.json");
        if (!resp.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        if (!doc.RootElement.TryGetProperty("subscriptions", out var subs)) subs = doc.RootElement.GetProperty("items");
        // Try different root names per docs: list uses items array? Actually /customers/{id}/subscriptions returns items? The doc for list-subscriptions uses items.subscriptions. For customer subscriptions it likely uses items too.
        // Try "items" array of objects with "subscription"
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                {
                    var prodHandle = sub.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() : null;
                    if (prodHandle == productHandle || sub.TryGetProperty("product_handle", out var ph) && ph.GetString() == productHandle)
                        return MapSubscription(sub);
                }
                else
                {
                    var prodHandle = item.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() : null;
                    if (prodHandle == productHandle)
                        return MapSubscription(item);
                }
            }
        }
        return null;
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string customerReferenceOrId, string productHandle)
    {
        ConfigureAuth();
        // Resolve customer id from reference if needed
        int customerId = int.Parse(customerReferenceOrId);
        try
        {
            if (!int.TryParse(customerReferenceOrId, out customerId))
            {
                var c = await FindOrCreateCustomerAsync(customerReferenceOrId, "", "", "");
                customerId = c.Id;
            }
        }
        catch { }

        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_id = customerId
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{BaseUrl}/subscriptions.json", content);
        resp.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var sub = doc.RootElement.GetProperty("subscription");
        return MapSubscription(sub);
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(string customerReferenceOrId)
    {
        ConfigureAuth();
        int customerId = int.Parse(customerReferenceOrId);
        try
        {
            if (!int.TryParse(customerReferenceOrId, out customerId))
            {
                var c = await FindOrCreateCustomerAsync(customerReferenceOrId, "", "", "");
                customerId = c.Id;
            }
        }
        catch { }
        var resp = await _http.GetAsync($"{BaseUrl}/customers/{customerId}/subscriptions.json");
        var result = new List<SubscriptionDto>();
        if (!resp.IsSuccessStatusCode) return result;
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("subscription", out var sub))
                    result.Add(MapSubscription(sub));
                else
                    result.Add(MapSubscription(item));
            }
        }
        return result;
    }

    private SubscriptionDto MapSubscription(JsonElement el)
    {
        return new SubscriptionDto
        {
            Id = el.GetProperty("id").GetInt32(),
            State = el.GetProperty("state").GetString() ?? "unknown",
            ProductHandle = el.TryGetProperty("product", out var p) ? (p.TryGetProperty("handle", out var h) ? h.GetString() : null) : null,
            PriceInCents = el.TryGetProperty("product_price_in_cents", out var pp) ? pp.GetInt64() : 0,
            CurrentPeriodEndsAt = el.TryGetProperty("current_period_ends_at", out var cpe) && cpe.ValueKind != JsonValueKind.Null ? cpe.GetString() : null,
            ActivatedAt = el.TryGetProperty("activated_at", out var aa) && aa.ValueKind != JsonValueKind.Null ? aa.GetString() : null,
            NextAssessmentAt = el.TryGetProperty("next_assessment_at", out var na) && na.ValueKind != JsonValueKind.Null ? na.GetString() : null
        };
    }
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public string? Description { get; set; }
    public int ProductId { get; set; }
}

public class MaxioCustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string? ProductHandle { get; set; }
    public long PriceInCents { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? NextAssessmentAt { get; set; }
}
