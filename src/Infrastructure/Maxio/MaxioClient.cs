using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioClient
{
    Task<List<PlanDto>> GetPlansAsync();
    Task<UserSubscriptionDto?> FindCustomerByReferenceAsync(string reference);
    Task<SubscriptionResponse?> CreateSubscriptionAsync(string customerReference, string productHandle, string email, string firstName, string lastName);
    Task<List<SubscriptionResponse>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> options)
    {
        _http = http;
        _settings = options.Value;
        var baseUrl = !string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";
        _http.BaseAddress = new Uri(baseUrl + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<PlanDto>> GetPlansAsync()
    {
        var family = _settings.ProductFamilyHandle;
        var url = $"product_families/{family}/products.json?per_page=50";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var items = new List<PlanDto>();
        if (doc.RootElement.TryGetProperty("items", out var rootItems))
        {
            foreach (var item in rootItems.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var prod))
                {
                    items.Add(new PlanDto
                    {
                        Id = prod.GetProperty("id").GetInt32(),
                        Name = prod.GetProperty("name").GetString() ?? "",
                        Handle = prod.GetProperty("handle").GetString() ?? "",
                        PriceInCents = prod.GetProperty("price_in_cents").GetInt64(),
                        IntervalUnit = prod.TryGetProperty("interval_unit", out var iu) ? iu.GetString() : "month",
                        Interval = prod.TryGetProperty("interval", out var i) ? i.GetInt32() : 1
                    });
                }
            }
        }
        return items;
    }

    public async Task<UserSubscriptionDto?> FindCustomerByReferenceAsync(string reference)
    {
        var url = $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _http.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.TryGetProperty("customer", out var cust))
        {
            return new UserSubscriptionDto
            {
                CustomerId = cust.GetProperty("id").GetInt32(),
                Reference = cust.GetProperty("reference").GetString() ?? reference,
                Email = cust.TryGetProperty("email", out var e) ? e.GetString() : null,
                FirstName = cust.TryGetProperty("first_name", out var fn) ? fn.GetString() : null,
                LastName = cust.TryGetProperty("last_name", out var ln) ? ln.GetString() : null
            };
        }
        return null;
    }

    public async Task<SubscriptionResponse?> CreateSubscriptionAsync(string customerReference, string productHandle, string email, string firstName, string lastName)
    {
        var payload = new SubscriptionCreatePayload
        {
            Subscription = new SubscriptionPayload
            {
                ProductHandle = productHandle,
                CustomerAttributes = new CustomerAttributesPayload
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = email,
                    Reference = customerReference
                }
            }
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("subscriptions.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Maxio create subscription failed: {response.StatusCode} {err}");
        }
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.TryGetProperty("subscription", out var sub))
        {
            return ParseSubscription(sub);
        }
        return null;
    }

    public async Task<List<SubscriptionResponse>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var url = $"customers/{customerId}/subscriptions.json?per_page=50";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var list = new List<SubscriptionResponse>();
        if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var s in subs.EnumerateArray())
                list.Add(ParseSubscription(s));
        }
        return list;
    }

    private static SubscriptionResponse ParseSubscription(JsonElement sub)
    {
        var state = sub.TryGetProperty("state", out var st) ? st.GetString() : "unknown";
        var productHandle = sub.TryGetProperty("product_handle", out var ph) ? ph.GetString() : null;
        var productName = sub.TryGetProperty("product", out var prod) && prod.TryGetProperty("name", out var pn) ? pn.GetString() : productHandle;
        var nextBilling = sub.TryGetProperty("next_billing_at", out var nb) ? nb.GetString() : null;
        return new SubscriptionResponse
        {
            Id = sub.GetProperty("id").GetInt32(),
            State = state,
            ProductHandle = productHandle,
            ProductName = productName,
            NextBillingAt = nextBilling
        };
    }
}

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public long PriceInCents { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public int Interval { get; set; } = 1;
}

public class UserSubscriptionDto
{
    public int CustomerId { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public string? NextBillingAt { get; set; }
}

public class SubscriptionCreatePayload
{
    [System.Text.Json.Serialization.JsonPropertyName("subscription")]
    public SubscriptionPayload Subscription { get; set; } = new();
}

public class SubscriptionPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("customer_attributes")]
    public CustomerAttributesPayload CustomerAttributes { get; set; } = new();
}

public class CustomerAttributesPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string Email { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("reference")]
    public string Reference { get; set; } = "";
}
