using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Settings;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public interface IMaxioApiClient
{
    Task<CustomerResponse?> FindOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<IEnumerable<PlanResponse>> GetPlansAsync();
    Task<SubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<IEnumerable<SubscriptionResponse>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioApiClient : IMaxioApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioSettings _settings;

    public MaxioApiClient(HttpClient httpClient, MaxioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.BaseAddress = new Uri(_settings.GetBaseUrl());
    }

    public async Task<CustomerResponse?> FindOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        var existing = await FindCustomerByReferenceAsync(userId);
        if (existing != null)
            return existing;

        var createRequest = new
        {
            customer = new
            {
                reference = userId,
                email,
                first_name = firstName,
                last_name = lastName
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/customers.json", content);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var jsonDoc = JsonSerializer.Deserialize<JsonElement>(responseContent, options);

        if (jsonDoc.TryGetProperty("customer", out var customerElement))
        {
            return JsonSerializer.Deserialize<CustomerResponse>(customerElement.GetRawText(), options);
        }

        return null;
    }

    public async Task<IEnumerable<PlanResponse>> GetPlansAsync()
    {
        var response = await _httpClient.GetAsync($"/product_families/{_settings.ProductFamilyHandle}/products.json");
        if (!response.IsSuccessStatusCode)
        {
            return Enumerable.Empty<PlanResponse>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var products = JsonSerializer.Deserialize<List<ProductWrapper>>(content, options) ?? new();

        return products.Select(p => p.Product);
    }

    public async Task<SubscriptionResponse?> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var createRequest = new
        {
            subscription = new
            {
                customer_id = customerId,
                product_handle = productHandle,
                payment_collection_method = "remittance"
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(createRequest),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/subscriptions.json", content);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var jsonDoc = JsonSerializer.Deserialize<JsonElement>(responseContent, options);

        if (jsonDoc.TryGetProperty("subscription", out var subscriptionElement))
        {
            return JsonSerializer.Deserialize<SubscriptionResponse>(subscriptionElement.GetRawText(), options);
        }

        return null;
    }

    public async Task<IEnumerable<SubscriptionResponse>> GetCustomerSubscriptionsAsync(int customerId)
    {
        var response = await _httpClient.GetAsync($"/subscriptions.json?customer_id={customerId}");
        if (!response.IsSuccessStatusCode)
        {
            return Enumerable.Empty<SubscriptionResponse>();
        }

        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var subscriptions = JsonSerializer.Deserialize<List<SubscriptionWrapper>>(content, options) ?? new();

        return subscriptions.Select(s => s.Subscription);
    }

    private async Task<CustomerResponse?> FindCustomerByReferenceAsync(string reference)
    {
        var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var jsonDoc = JsonSerializer.Deserialize<JsonElement>(content, options);

        if (jsonDoc.TryGetProperty("customer", out var customerElement))
        {
            return JsonSerializer.Deserialize<CustomerResponse>(customerElement.GetRawText(), options);
        }

        return null;
    }
}

public class CustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Reference { get; set; }
}

public class PlanResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = null!;
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ProductResponse Product { get; set; } = null!;
}

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public int PriceInCents { get; set; }
}

public class ProductWrapper
{
    public PlanResponse Product { get; set; } = null!;
}

public class SubscriptionWrapper
{
    public SubscriptionResponse Subscription { get; set; } = null!;
}
