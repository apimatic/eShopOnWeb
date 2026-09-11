using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioService
{
    Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync();
    Task<CustomerInfo?> GetCustomerByReferenceAsync(string reference);
    Task<CustomerInfo> CreateCustomerAsync(string reference, string email, string firstName, string lastName);
    Task<SubscriptionInfo> CreateSubscriptionAsync(string productHandle, string customerReference);
    Task<List<SubscriptionInfo>> GetSubscriptionsByReferenceAsync(string customerReference);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IOptions<MaxioSettings> settings, ILogger<MaxioService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl.TrimEnd('/')
        : $"https://{_settings.Subdomain}.chargify.com";

    public async Task<List<SubscriptionPlan>> GetSubscriptionPlansAsync()
    {
        var url = $"{BaseUrl}/product_families/handle:{_settings.ProductFamilyHandle}/products.json";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var plans = new List<SubscriptionPlan>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var prod))
                    plans.Add(MapProduct(prod));
            }
        }
        else if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var prod))
                    plans.Add(MapProduct(prod));
            }
        }
        return plans;
    }

    private SubscriptionPlan MapProduct(JsonElement prod)
    {
        return new SubscriptionPlan
        {
            Handle = prod.GetProperty("handle").GetString() ?? "",
            Name = prod.GetProperty("name").GetString() ?? "",
            PriceInCents = prod.TryGetProperty("price_in_cents", out var p) ? p.GetInt64() : 0,
            Interval = prod.TryGetProperty("interval", out var i) ? i.GetInt32() : 0,
            IntervalUnit = prod.TryGetProperty("interval_unit", out var iu) ? iu.GetString() ?? "" : ""
        };
    }

    public async Task<CustomerInfo?> GetCustomerByReferenceAsync(string reference)
    {
        var url = $"{BaseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        if (doc == null) return null;
        var customer = doc["customer"]?.AsObject();
        if (customer == null) return null;
        return MapCustomer(customer);
    }

    public async Task<CustomerInfo> CreateCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var existing = await GetCustomerByReferenceAsync(reference);
        if (existing != null) return existing;

        var payload = new
        {
            customer = new
            {
                reference,
                email,
                first_name = firstName,
                last_name = lastName,
                country = "US"
            }
        };
        var url = $"{BaseUrl}/customers.json";
        var response = await _http.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var customer = doc?["customer"]?.AsObject();
        return customer == null ? throw new InvalidOperationException("Customer creation returned no data") : MapCustomer(customer);
    }

    public async Task<SubscriptionInfo> CreateSubscriptionAsync(string productHandle, string customerReference)
    {
        var payload = new
        {
            subscription = new
            {
                product_handle = productHandle,
                customer_reference = customerReference
            }
        };
        var url = $"{BaseUrl}/subscriptions.json";
        var response = await _http.PostAsJsonAsync(url, payload);
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            var errText = await response.Content.ReadAsStringAsync();
            _logger.LogError("Subscription creation error: {Text}", errText);
        }
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonObject>();
        var sub = doc?["subscription"]?.AsObject();
        return sub == null ? throw new InvalidOperationException("Subscription creation returned no data") : MapSubscription(sub);
    }

    public async Task<List<SubscriptionInfo>> GetSubscriptionsByReferenceAsync(string customerReference)
    {
        var url = $"{BaseUrl}/subscriptions.json?customer_reference={Uri.EscapeDataString(customerReference)}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var array = JsonDocument.Parse(content).RootElement;
        var list = new List<SubscriptionInfo>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.TryGetProperty("subscription", out var sub))
                list.Add(MapSubscription(sub));
            else
                list.Add(MapSubscription(item));
        }
        return list;
    }

    private CustomerInfo MapCustomer(JsonObject obj)
    {
        return new CustomerInfo
        {
            Id = obj["id"]?.GetValue<int>() ?? 0,
            Reference = obj["reference"]?.GetValue<string>() ?? "",
            Email = obj["email"]?.GetValue<string>() ?? "",
            FirstName = obj["first_name"]?.GetValue<string>() ?? "",
            LastName = obj["last_name"]?.GetValue<string>() ?? ""
        };
    }

    private SubscriptionInfo MapSubscription(JsonObject sub)
    {
        return new SubscriptionInfo
        {
            Id = sub["id"]?.GetValue<int>() ?? 0,
            State = sub["state"]?.GetValue<string>() ?? "",
            ProductHandle = sub["product_handle"]?.GetValue<string>() ?? (sub["product"] is JsonObject p ? p["handle"]?.GetValue<string>() ?? "" : ""),
            ProductName = sub["product"] is JsonObject p2 ? p2["name"]?.GetValue<string>() ?? "" : "",
            PriceInCents = sub["product_price_in_cents"]?.GetValue<long>() ?? 0,
            CurrentPeriodEndsAt = sub["current_period_ends_at"]?.GetValue<string>() ?? "",
            NextAssessmentAt = sub["next_assessment_at"]?.GetValue<string>() ?? "",
            ActivatedAt = sub["activated_at"]?.GetValue<string>() ?? ""
        };
    }

    private SubscriptionInfo MapSubscription(JsonElement sub)
    {
        return new SubscriptionInfo
        {
            Id = sub.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            State = sub.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "",
            ProductHandle = sub.TryGetProperty("product_handle", out var ph) ? ph.GetString() ?? "" : (sub.TryGetProperty("product", out var prod) ? prod.GetProperty("handle").GetString() ?? "" : ""),
            ProductName = sub.TryGetProperty("product", out var p2) ? p2.GetProperty("name").GetString() ?? "" : "",
            PriceInCents = sub.TryGetProperty("product_price_in_cents", out var pp) ? pp.GetInt64() : 0,
            CurrentPeriodEndsAt = sub.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() ?? "" : "",
            NextAssessmentAt = sub.TryGetProperty("next_assessment_at", out var na) ? na.GetString() ?? "" : "",
            ActivatedAt = sub.TryGetProperty("activated_at", out var aa) ? aa.GetString() ?? "" : ""
        };
    }
}

public class SubscriptionPlan
{
    public string Handle { get; set; } = "";
    public string Name { get; set; } = "";
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "";

    public decimal Price => PriceInCents / 100m;
}

public class CustomerInfo
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

public class SubscriptionInfo
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public string ProductName { get; set; } = "";
    public long PriceInCents { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = "";
    public string NextAssessmentAt { get; set; } = "";
    public string ActivatedAt { get; set; } = "";

    public decimal Price => PriceInCents / 100m;
}
