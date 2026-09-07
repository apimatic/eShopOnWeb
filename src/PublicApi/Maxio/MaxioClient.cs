using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioClient
{
    Task<CustomerResponse?> LookupCustomerAsync(string reference);
    Task<CustomerResponse?> CreateCustomerAsync(CreateCustomerRequest request);
    Task<ListProductsResponse?> ListProductsAsync(string productFamilyHandle);
    Task<SubscriptionResponse?> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request);
    Task<ListSubscriptionsResponse?> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public MaxioClient(HttpClient httpClient, MaxioConfiguration config, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        SetupHttpClient();
    }

    private void SetupHttpClient()
    {
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ApiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.BaseAddress = new Uri(_config.GetApiBaseUrl());
    }

    public async Task<CustomerResponse?> LookupCustomerAsync(string reference)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
                if (result.TryGetProperty("customer", out var customerData))
                {
                    return JsonSerializer.Deserialize<CustomerResponse>(customerData.GetRawText(), JsonOptions);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up customer by reference: {reference}", reference);
            throw;
        }
    }

    public async Task<CustomerResponse?> CreateCustomerAsync(CreateCustomerRequest request)
    {
        try
        {
            var payload = new { customer = request };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/customers.json", content);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson, JsonOptions);
                if (result.TryGetProperty("customer", out var customerData))
                {
                    return JsonSerializer.Deserialize<CustomerResponse>(customerData.GetRawText(), JsonOptions);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer");
            throw;
        }
    }

    public async Task<ListProductsResponse?> ListProductsAsync(string productFamilyHandle)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/product_families/lookup.json?handle={Uri.EscapeDataString(productFamilyHandle)}");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
                if (result.TryGetProperty("product_family", out var familyData))
                {
                    var productList = familyData.GetProperty("products");
                    return new ListProductsResponse { Products = JsonSerializer.Deserialize<ProductResponse[]>(productList.GetRawText(), JsonOptions) ?? Array.Empty<ProductResponse>() };
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products for family: {handle}", productFamilyHandle);
            throw;
        }
    }

    public async Task<SubscriptionResponse?> CreateSubscriptionAsync(MaxioCreateSubscriptionRequest request)
    {
        try
        {
            var payload = new { subscription = request };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/subscriptions.json", content);
            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(responseJson, JsonOptions);
                if (result.TryGetProperty("subscription", out var subscriptionData))
                {
                    return JsonSerializer.Deserialize<SubscriptionResponse>(subscriptionData.GetRawText(), JsonOptions);
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error creating subscription. Status: {status}, Content: {content}", response.StatusCode, errorContent);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            throw;
        }
    }

    public async Task<ListSubscriptionsResponse?> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/customers/{customerId}/subscriptions.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ListSubscriptionsResponse>(json, JsonOptions);
                return result;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing customer subscriptions for customer: {customerId}", customerId);
            throw;
        }
    }
}

// DTOs for Maxio API responses
public class CustomerResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateCustomerRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool Taxable { get; set; }
    public bool RequireCreditCard { get; set; }
}

public class ListProductsResponse
{
    public ProductResponse[] Products { get; set; } = Array.Empty<ProductResponse>();
}

public class MaxioCreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public int CustomerId { get; set; }
}

public class SubscriptionResponse
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public ProductResponse? Product { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ListSubscriptionsResponse
{
    public SubscriptionResponse[] Subscriptions { get; set; } = Array.Empty<SubscriptionResponse>();
}
