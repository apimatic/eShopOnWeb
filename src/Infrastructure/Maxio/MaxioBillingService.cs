using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public interface IMaxioBillingService
{
    Task<IReadOnlyList<PlanDto>> GetPlansAsync();
    Task<SubscriptionResultDto> SubscribeAsync(string userReference, string firstName, string lastName, string email, string productHandle);
    Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference);
}

public class PlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Interval { get; set; } = string.Empty;
}

public class SubscriptionResultDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string NextBillingDate { get; set; } = string.Empty;
    public bool Created { get; set; }
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CurrentPeriodEndsAt { get; set; } = string.Empty;
    public string NextBillingDate { get; set; } = string.Empty;
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MaxioBillingService(HttpClient http, MaxioSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    private string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl.TrimEnd('/')
        : $"https://{_settings.Subdomain}.chargify.com";

    private void ConfigureAuth()
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:X")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync()
    {
        ConfigureAuth();
        var url = $"{BaseUrl}/product_families/handle:{_settings.ProductFamilyHandle}/products.json?per_page=200";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = new List<PlanDto>();
        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("product", out var prod))
                {
                    list.Add(new PlanDto
                    {
                        Handle = prod.GetProperty("handle").GetString() ?? prod.GetProperty("id").ToString(),
                        Name = prod.GetProperty("name").GetString() ?? "",
                        Price = prod.TryGetProperty("price_in_cents", out var pc) ? pc.GetInt64() / 100m : 0m,
                        Interval = prod.TryGetProperty("interval_unit", out var iu) ? iu.GetString() ?? "month" : "month"
                    });
                }
            }
        }
        return list;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userReference, string firstName, string lastName, string email, string productHandle)
    {
        ConfigureAuth();
        // Idempotent customer lookup
        int customerId = await GetOrCreateCustomerAsync(userReference, firstName, lastName, email);

        // Check existing subscription for same product to avoid duplicates
        var existing = await FindSubscriptionAsync(customerId, productHandle);
        if (existing != null)
        {
            return new SubscriptionResultDto
            {
                SubscriptionId = existing.Id,
                State = existing.State,
                ProductName = existing.ProductName,
                Price = existing.Price,
                NextBillingDate = existing.NextBillingDate,
                Created = false
            };
        }

        var payload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["product_handle"] = productHandle,
                ["customer_reference"] = userReference
            }
        };

        var url = $"{BaseUrl}/subscriptions.json";
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(url, content);
        resp.EnsureSuccessStatusCode();
        var respJson = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        var sub = doc.RootElement.GetProperty("subscription");
        return new SubscriptionResultDto
        {
            SubscriptionId = sub.GetProperty("id").GetInt32(),
            State = sub.GetProperty("state").GetString() ?? "",
            ProductName = sub.GetProperty("product").GetProperty("name").GetString() ?? "",
            Price = sub.TryGetProperty("product_price_in_cents", out var ppc) ? ppc.GetInt64() / 100m : 0m,
            NextBillingDate = sub.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() ?? "" : "",
            Created = true
        };
    }

    public async Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference)
    {
        ConfigureAuth();
        int customerId = await FindCustomerIdAsync(userReference);
        if (customerId == 0) return new List<MySubscriptionDto>();

        var url = $"{BaseUrl}/customers/{customerId}/subscriptions.json?per_page=50";
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new List<MySubscriptionDto>();
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var list = new List<MySubscriptionDto>();
        if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
        {
            foreach (var s in subs.EnumerateArray())
            {
                JsonElement? prod = null;
                if (s.TryGetProperty("product", out var p)) prod = p;
                list.Add(new MySubscriptionDto
                {
                    Id = s.GetProperty("id").GetInt32(),
                    ProductName = prod?.GetProperty("name").GetString() ?? "",
                    ProductHandle = prod?.GetProperty("handle").GetString() ?? "",
                    State = s.GetProperty("state").GetString() ?? "",
                    Price = s.TryGetProperty("product_price_in_cents", out var pc) ? pc.GetInt64() / 100m : 0m,
                    CurrentPeriodEndsAt = s.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() ?? "" : ""
                });
            }
        }
        return list;
    }

    private async Task<int> GetOrCreateCustomerAsync(string reference, string first, string last, string email)
    {
        var id = await FindCustomerIdAsync(reference);
        if (id > 0) return id;

        var payload = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["first_name"] = first,
                ["last_name"] = last,
                ["email"] = email,
                ["reference"] = reference
            }
        };
        var url = $"{BaseUrl}/customers.json";
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync(url, content);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("customer").GetProperty("id").GetInt32();
    }

    private async Task<int> FindCustomerIdAsync(string reference)
    {
        try
        {
            ConfigureAuth();
            var url = $"{BaseUrl}/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
            var resp = await _http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("customer", out var c))
                {
                    return c.GetProperty("id").GetInt32();
                }
            }
        }
        catch { }
        return 0;
    }

    private async Task<MySubscriptionDto?> FindSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            ConfigureAuth();
            var url = $"{BaseUrl}/customers/{customerId}/subscriptions.json?per_page=20";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("subscriptions", out var subs))
            {
                foreach (var s in subs.EnumerateArray())
                {
                    if (s.TryGetProperty("product", out var prod))
                    {
                        var handle = prod.GetProperty("handle").GetString() ?? "";
                        if (!string.IsNullOrEmpty(handle) && handle.Equals(productHandle, StringComparison.OrdinalIgnoreCase))
                        {
                            return new MySubscriptionDto
                            {
                                Id = s.GetProperty("id").GetInt32(),
                                ProductName = prod.GetProperty("name").GetString() ?? "",
                                ProductHandle = handle,
                                State = s.GetProperty("state").GetString() ?? "",
                                Price = s.TryGetProperty("product_price_in_cents", out var pc) ? pc.GetInt64() / 100m : 0m,
                                CurrentPeriodEndsAt = s.TryGetProperty("current_period_ends_at", out var cpe) ? cpe.GetString() ?? "" : ""
                            };
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }
}
