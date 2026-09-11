using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

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
        var baseUrl = !string.IsNullOrEmpty(_settings.BaseUrl)
            ? _settings.BaseUrl.TrimEnd('/')
            : $"https://{_settings.Subdomain}.chargify.com";
        _http.BaseAddress = new Uri(baseUrl + "/");
        var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        var family = await GetFamilyAsync();
        if (family == null) return new List<SubscriptionPlanDto>();
        var response = await _http.GetAsync($"products.json?family_id={family.Id}");
        response.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var plans = new List<SubscriptionPlanDto>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var prod = item.GetProperty("product");
                plans.Add(new SubscriptionPlanDto
                {
                    Handle = prod.GetProperty("handle").GetString() ?? string.Empty,
                    Name = prod.GetProperty("name").GetString() ?? string.Empty,
                    PriceInCents = prod.GetProperty("price_in_cents").GetInt32(),
                    Currency = prod.TryGetProperty("currency", out var c) ? c.GetString() ?? "USD" : "USD",
                    Interval = prod.TryGetProperty("interval", out var i) ? i.GetInt32() : 1,
                    IntervalUnit = prod.TryGetProperty("interval_unit", out var iu) ? iu.GetString() ?? "month" : "month"
                });
            }
        }
        return plans;
    }

    private async Task<ProductFamilyInfo?> GetFamilyAsync()
    {
        var response = await _http.GetAsync($"product_families.json?handle={_settings.ProductFamilyHandle}");
        if (!response.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            var family = doc.RootElement[0].GetProperty("product_family");
            return new ProductFamilyInfo
            {
                Id = family.GetProperty("id").GetInt32(),
                Handle = family.GetProperty("handle").GetString() ?? string.Empty
            };
        }
        return null;
    }

    public async Task<SubscriptionResultDto> SubscribeAsync(string userReference, string email, string firstName, string lastName, string productHandle)
    {
        if (string.IsNullOrWhiteSpace(productHandle)) productHandle = "eshop-pro";
        var customer = await EnsureCustomerAsync(userReference, email, firstName, lastName);
        var existing = await FindSubscriptionAsync(customer.Id.ToString(), productHandle);
        if (existing != null) return existing;

        var payload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["customer_id"] = customer.Id,
                ["product_handle"] = productHandle,
                ["auto_charge"] = false,
                ["payment_collection_method"] = "remittance"
            }
        };
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("subscriptions.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio subscription creation failed: {Status} {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"Subscription creation failed: {response.StatusCode} - {err}");
        }
        var subDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return MapSubscription(subDoc.RootElement.GetProperty("subscription"), customer);
    }

    public async Task<IReadOnlyList<SubscriptionResultDto>> GetMySubscriptionsAsync(string userReference)
    {
        var customer = await FindCustomerAsync(userReference);
        if (customer == null) return new List<SubscriptionResultDto>();
        var response = await _http.GetAsync($"subscriptions.json?customer_id={customer.Id}");
        if (!response.IsSuccessStatusCode) return new List<SubscriptionResultDto>();
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var result = new List<SubscriptionResultDto>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var sub = item.GetProperty("subscription");
                result.Add(MapSubscription(sub, customer));
            }
        }
        return result;
    }

    private async Task<SubscriptionResultDto?> FindSubscriptionAsync(string customerId, string productHandle)
    {
        var response = await _http.GetAsync($"subscriptions.json?customer_id={customerId}");
        if (!response.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var sub = item.GetProperty("subscription");
                var prod = sub.GetProperty("product");
                var handle = prod.GetProperty("handle").GetString() ?? string.Empty;
                if (string.Equals(handle, productHandle, StringComparison.OrdinalIgnoreCase))
                {
                    var cust = await FindCustomerAsyncById(customerId);
                    return MapSubscription(sub, cust ?? new CustomerInfo { Id = int.Parse(customerId) });
                }
            }
        }
        return null;
    }

    private async Task<CustomerInfo> EnsureCustomerAsync(string reference, string email, string firstName, string lastName)
    {
        var existing = await FindCustomerAsync(reference);
        if (existing != null) return existing;
        var payload = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["email"] = email ?? $"{reference}@example.com",
                ["first_name"] = firstName ?? "User",
                ["last_name"] = lastName ?? "Shop",
                ["reference"] = reference
            }
        };
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("customers.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Maxio customer creation failed: {Status} {Body}", response.StatusCode, err);
            throw new InvalidOperationException($"Customer creation failed: {response.StatusCode} - {err}");
        }
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var cust = doc.RootElement.GetProperty("customer");
        return new CustomerInfo
        {
            Id = cust.GetProperty("id").GetInt32(),
            Email = cust.GetProperty("email").GetString() ?? string.Empty,
            Reference = cust.GetProperty("reference").GetString() ?? reference
        };
    }

    private async Task<CustomerInfo?> FindCustomerAsync(string reference)
    {
        var response = await _http.GetAsync($"customers.json?reference={reference}");
        if (!response.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("customer", out var cust))
        {
            return new CustomerInfo
            {
                Id = cust.GetProperty("id").GetInt32(),
                Email = cust.GetProperty("email").GetString() ?? string.Empty,
                Reference = cust.GetProperty("reference").GetString() ?? reference
            };
        }
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            var first = doc.RootElement[0].GetProperty("customer");
            return new CustomerInfo
            {
                Id = first.GetProperty("id").GetInt32(),
                Email = first.GetProperty("email").GetString() ?? string.Empty,
                Reference = first.GetProperty("reference").GetString() ?? reference
            };
        }
        return null;
    }

    private async Task<CustomerInfo?> FindCustomerAsyncById(string id)
    {
        var response = await _http.GetAsync($"customers/{id}.json");
        if (!response.IsSuccessStatusCode) return null;
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var cust = doc.RootElement.GetProperty("customer");
        return new CustomerInfo
        {
            Id = cust.GetProperty("id").GetInt32(),
            Email = cust.GetProperty("email").GetString() ?? string.Empty,
            Reference = cust.GetProperty("reference").GetString() ?? string.Empty
        };
    }

    private SubscriptionResultDto MapSubscription(JsonElement sub, CustomerInfo customer)
    {
        var product = sub.GetProperty("product");
        return new SubscriptionResultDto
        {
            SubscriptionId = sub.GetProperty("id").GetInt32(),
            State = sub.GetProperty("state").GetString() ?? string.Empty,
            PlanName = product.GetProperty("name").GetString() ?? string.Empty,
            PlanHandle = product.GetProperty("handle").GetString() ?? string.Empty,
            PriceInCents = sub.GetProperty("product_price_in_cents").GetInt32(),
            Currency = sub.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "USD" : "USD",
            NextBillingDate = sub.GetProperty("current_period_ends_at").GetString() ?? string.Empty,
            CustomerReference = customer.Reference
        };
    }

    private class ProductFamilyInfo
    {
        public int Id { get; set; }
        public string Handle { get; set; } = string.Empty;
    }

    private class CustomerInfo
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }
}
