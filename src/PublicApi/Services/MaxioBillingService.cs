using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioBillingService
{
    Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResult> SubscribeAsync(string userReference, string userEmail, string productHandle, CancellationToken ct = default);
    Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MaxioBillingService(HttpClient httpClient, IOptions<MaxioConfiguration> options)
    {
        _httpClient = httpClient;
        _config = options.Value;
        var baseUrl = !string.IsNullOrWhiteSpace(_config.BaseUrl)
            ? _config.BaseUrl.TrimEnd('/')
            : $"https://{_config.Subdomain}.chargify.com";
        _httpClient.BaseAddress = new Uri(baseUrl + "/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(_config.ApiKey + ":")));
    }

    public async Task<List<SubscriptionPlanDto>> GetSubscriptionPlansAsync(CancellationToken ct = default)
    {
        // Verify endpoint via public contract: product families / products listed by handle
        // We query the family by handle and list its products/components
        var familyHandle = _config.ProductFamilyHandle;
        var resp = await _httpClient.GetAsync($"api/v2/product_families.json?handle={familyHandle}", ct);
        if (!resp.IsSuccessStatusCode) return new List<SubscriptionPlanDto>();
        var family = await resp.Content.ReadFromJsonAsync<MaxioFamilyResponse>(_jsonOptions, ct);
        if (family?.Result == null) return new List<SubscriptionPlanDto>();

        // Fetch products in family for plan listing
        var productsResp = await _httpClient.GetAsync($"api/v2/products.json?product_family_id={family.Result.Id}", ct);
        var products = new List<SubscriptionPlanDto>();
        if (productsResp.IsSuccessStatusCode)
        {
            var pr = await productsResp.Content.ReadFromJsonAsync<MaxioProductListResponse>(_jsonOptions, ct);
            if (pr?.Result != null)
            {
                foreach (var p in pr.Result)
                {
                    products.Add(new SubscriptionPlanDto
                    {
                        Handle = p.Handle,
                        Name = p.Name,
                        Price = p.PriceInCents / 100.0m,
                        FamilyHandle = familyHandle
                    });
                }
            }
        }
        return products;
    }

    public async Task<SubscriptionResult> SubscribeAsync(string userReference, string userEmail, string productHandle, CancellationToken ct = default)
    {
        // Idempotent customer lookup by reference
        var customer = await GetOrCreateCustomerAsync(userReference, userEmail, ct);

        // Idempotent subscription check
        var existing = await GetSubscriptionByCustomerAndProductAsync(customer.Id, productHandle, ct);
        if (existing != null)
        {
            return new SubscriptionResult
            {
                Success = true,
                CustomerReference = userReference,
                PlanHandle = productHandle,
                State = existing.State,
                NextBillingDate = existing.NextBillingAt
            };
        }

        // Create subscription via confirmed v2 endpoint
        var payload = new
        {
            subscription = new
            {
                customer_reference = userReference,
                product_handle = productHandle,
                state = "active"
            }
        };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync("api/v2/subscriptions.json", content, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            return new SubscriptionResult { Success = false, Error = err };
        }
        var subResp = await resp.Content.ReadFromJsonAsync<MaxioSubscriptionResponse>(_jsonOptions, ct);
        var sub = subResp?.Subscription ?? subResp?.Result;
        return new SubscriptionResult
        {
            Success = true,
            CustomerReference = userReference,
            PlanHandle = productHandle,
            State = sub?.State ?? "active",
            NextBillingDate = sub?.NextBillingAt ?? DateTime.UtcNow.AddMonths(1)
        };
    }

    public async Task<List<MySubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default)
    {
        var resp = await _httpClient.GetAsync($"api/v2/subscriptions.json?customer_reference={userReference}&per_page=50", ct);
        if (!resp.IsSuccessStatusCode) return new List<MySubscriptionDto>();
        var data = await resp.Content.ReadFromJsonAsync<MaxioSubscriptionListResponse>(_jsonOptions, ct);
        var list = new List<MySubscriptionDto>();
        if (data?.Result != null)
        {
            foreach (var s in data.Result)
            {
                list.Add(new MySubscriptionDto
                {
                    Id = s.Id,
                    ProductHandle = s.ProductHandle,
                    State = s.State,
                    NextBillingDate = s.NextBillingAt
                });
            }
        }
        return list;
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string reference, string email, CancellationToken ct)
    {
        var search = await _httpClient.GetAsync($"api/v2/customers.json?reference={reference}&per_page=1", ct);
        if (search.IsSuccessStatusCode)
        {
            var list = await search.Content.ReadFromJsonAsync<MaxioCustomerListResponse>(_jsonOptions, ct);
            if (list?.Result?.FirstOrDefault() != null)
                return list.Result.First();
        }
        // Create
        var payload = new { customer = new { reference, email, first_name = "", last_name = "" } };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var create = await _httpClient.PostAsync("api/v2/customers.json", content, ct);
        if (create.IsSuccessStatusCode)
        {
            var c = await create.Content.ReadFromJsonAsync<MaxioCustomerResponse>(_jsonOptions, ct);
            return c?.Customer ?? c?.Result ?? new MaxioCustomer { Id = 0, Reference = reference };
        }
        return new MaxioCustomer { Id = 0, Reference = reference };
    }

    private async Task<MaxioSubscription?> GetSubscriptionByCustomerAndProductAsync(int customerId, string productHandle, CancellationToken ct)
    {
        var resp = await _httpClient.GetAsync($"api/v2/subscriptions.json?customer_id={customerId}&per_page=50", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var data = await resp.Content.ReadFromJsonAsync<MaxioSubscriptionListResponse>(_jsonOptions, ct);
        return data?.Result?.FirstOrDefault(s => s.ProductHandle == productHandle);
    }
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string FamilyHandle { get; set; } = string.Empty;
}

public class SubscriptionResult
{
    public bool Success { get; set; }
    public string? CustomerReference { get; set; }
    public string? PlanHandle { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? Error { get; set; }
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
}

public class MaxioFamilyResponse
{
    [JsonPropertyName("result")] public MaxioFamily? Result { get; set; }
}
public class MaxioFamily
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

public class MaxioProductListResponse
{
    [JsonPropertyName("result")] public List<MaxioProduct> Result { get; set; } = new();
}
public class MaxioProduct
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("handle")] public string Handle { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("price_in_cents")] public int PriceInCents { get; set; }
    [JsonPropertyName("product_family_id")] public int? ProductFamilyId { get; set; }
}

public class MaxioCustomerResponse
{
    [JsonPropertyName("customer")] public MaxioCustomer? Customer { get; set; }
    [JsonPropertyName("result")] public MaxioCustomer? Result { get; set; }
}
public class MaxioCustomerListResponse
{
    [JsonPropertyName("result")] public List<MaxioCustomer> Result { get; set; } = new();
}
public class MaxioCustomer
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")] public MaxioSubscription? Subscription { get; set; }
    [JsonPropertyName("result")] public MaxioSubscription? Result { get; set; }
}
public class MaxioSubscriptionListResponse
{
    [JsonPropertyName("result")] public List<MaxioSubscription> Result { get; set; } = new();
}
public class MaxioSubscription
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = "active";
    [JsonPropertyName("product_handle")] public string ProductHandle { get; set; } = string.Empty;
    [JsonPropertyName("next_billing_at")] public DateTime? NextBillingAt { get; set; }
    [JsonPropertyName("customer_reference")] public string CustomerReference { get; set; } = string.Empty;
}
