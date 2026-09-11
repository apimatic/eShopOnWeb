using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioBillingService : IMaxioBillingService
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioBillingService> _logger;

    public MaxioBillingService(IConfiguration configuration, ILogger<MaxioBillingService> logger)
    {
        _logger = logger;
        _settings = new MaxioSettings
        {
            ApiKey = configuration["Maxio:ApiKey"] ?? string.Empty,
            Subdomain = configuration["Maxio:Subdomain"] ?? string.Empty,
            ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"] ?? string.Empty,
            BaseUrl = configuration["Maxio:BaseUrl"] ?? string.Empty
        };

        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? $"https://{_settings.Subdomain}.chargify.com"
            : _settings.BaseUrl.TrimEnd('/');

        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl + "/") };
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.ApiKey}:"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<MaxioPlanDto>> GetPlansAsync()
    {
        // Maxio (Chargify) exposes products under the family via /api/v2/products.json
        // We filter client-side by family handle for simplicity.
        var response = await _httpClient.GetAsync("api/v2/products.json");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Maxio products call failed: {Status}", response.StatusCode);
            return new List<MaxioPlanDto>();
        }

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)?["products"];
        var plans = new List<MaxioPlanDto>();
        if (root is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is null) continue;
                var familyHandle = item["product_family"]?["handle"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(_settings.ProductFamilyHandle) &&
                    !familyHandle.Equals(_settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                    continue;

                plans.Add(new MaxioPlanDto
                {
                    Id = int.Parse(item["id"]?.ToString() ?? "0"),
                    Handle = item["handle"]?.ToString() ?? string.Empty,
                    Name = item["name"]?.ToString() ?? string.Empty,
                    Price = item["price_in_cents"] != null ? decimal.Parse(item["price_in_cents"]?.ToString() ?? "0") / 100m : 0m,
                    FamilyHandle = familyHandle
                });
            }
        }
        return plans;
    }

    public async Task<MaxioSubscriptionDto?> SubscribeAsync(string customerReference, string productHandle)
    {
        // Idempotent customer: lookup by reference
        var customer = await FindCustomerByReferenceAsync(customerReference);
        int customerId;
        if (customer == null)
        {
            customerId = await CreateCustomerAsync(customerReference);
        }
        else
        {
            customerId = int.Parse(customer["id"]?.ToString() ?? "0");
        }

        // Idempotent subscription: check existing for customer + product
        var existing = await FindSubscriptionAsync(customerId, productHandle);
        if (existing != null)
        {
            return existing;
        }

        var payload = new JsonObject
        {
            ["subscription"] = new JsonObject
            {
                ["customer_id"] = customerId,
                ["product_handle"] = productHandle,
                ["credit_card_attributes"] = new JsonObject { ["full_number"] = "4111111111111111" } // placeholder; sandbox allows no card
            }
        };

        // Note: task says payment method not required, but Chargify v2 may still require card object or skip.
        // We send minimal payload and handle errors gracefully.
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/v2/subscriptions.json", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Subscription creation failed: {Status} {Body}", response.StatusCode, err);
            return null;
        }

        var result = await response.Content.ReadAsStringAsync();
        return ToSubscriptionDto(result);
    }

    public async Task<IReadOnlyList<MaxioSubscriptionDto>> GetSubscriptionsAsync(string customerReference)
    {
        var customer = await FindCustomerByReferenceAsync(customerReference);
        if (customer == null) return new List<MaxioSubscriptionDto>();
        var customerId = int.Parse(customer["id"]?.ToString() ?? "0");

        var response = await _httpClient.GetAsync($"api/v2/subscriptions.json?customer_id={customerId}");
        if (!response.IsSuccessStatusCode) return new List<MaxioSubscriptionDto>();

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)?["subscriptions"];
        var list = new List<MaxioSubscriptionDto>();
        if (root is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item == null) continue;
                list.Add(ToSubscriptionDto(item));
            }
        }
        return list;
    }

    private async Task<JsonNode?> FindCustomerByReferenceAsync(string reference)
    {
        // Chargify lookup endpoint
        var response = await _httpClient.GetAsync($"api/v2/customers/lookup.json?reference={reference}");
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json)?["customer"];
            return root;
        }
        return null;
    }

    private async Task<int> CreateCustomerAsync(string reference)
    {
        var payload = new JsonObject
        {
            ["customer"] = new JsonObject
            {
                ["first_name"] = "User",
                ["last_name"] = reference,
                ["email"] = reference,
                ["reference"] = reference
            }
        };
        var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("api/v2/customers.json", content);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Customer creation failed: {Status} {Body}", response.StatusCode, err);
            throw new InvalidOperationException("Failed to create Maxio customer.");
        }
        var json = await response.Content.ReadAsStringAsync();
        var root = JsonNode.Parse(json)?["customer"];
        return int.Parse(root?["id"]?.ToString() ?? "0");
    }

    private async Task<MaxioSubscriptionDto?> FindSubscriptionAsync(int customerId, string productHandle)
    {
        var response = await _httpClient.GetAsync($"api/v2/subscriptions.json?customer_id={customerId}");
        if (!response.IsSuccessStatusCode) return null;
        var json = await response.Content.ReadAsStringAsync();
        var arr = JsonNode.Parse(json)?["subscriptions"] as JsonArray;
        if (arr == null) return null;
        foreach (var item in arr)
        {
            if (item == null) continue;
            var handle = item["product_handle"]?.ToString() ?? string.Empty;
            if (handle.Equals(productHandle, StringComparison.OrdinalIgnoreCase))
                return ToSubscriptionDto(item);
        }
        return null;
    }

    private MaxioSubscriptionDto ToSubscriptionDto(JsonNode node)
    {
        return new MaxioSubscriptionDto
        {
            Id = int.Parse(node["id"]?.ToString() ?? "0"),
            CustomerReference = node["customer"]?["reference"]?.ToString() ?? string.Empty,
            ProductHandle = node["product_handle"]?.ToString() ?? string.Empty,
            State = node["state"]?.ToString() ?? string.Empty,
            NextBillingDate = node["next_billing_date"] != null ? DateTime.Parse(node["next_billing_date"]?.ToString() ?? "") : null,
            Price = node["product_price_in_cents"] != null ? decimal.Parse(node["product_price_in_cents"]?.ToString() ?? "0") / 100m : 0m
        };
    }

    private MaxioSubscriptionDto? ToSubscriptionDto(string json)
    {
        var root = JsonNode.Parse(json)?["subscription"];
        if (root == null) return null;
        return ToSubscriptionDto(root);
    }
}
