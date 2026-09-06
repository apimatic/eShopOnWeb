using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<List<ProductDto>> GetProductsAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userReference, string firstName, string lastName, string email);
    Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, int customerId, string userReference);
    Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(HttpClient httpClient, string apiKey, string baseUrl, ILogger<MaxioService> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _logger = logger;

        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x"));
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/products.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var productsResponse = JsonSerializer.Deserialize<ProductsResponse>(content, options);

            return productsResponse?.Products ?? new List<ProductDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching products from Maxio");
            throw;
        }
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userReference, string firstName, string lastName, string email)
    {
        try
        {
            var getResponse = await _httpClient.GetAsync($"{_baseUrl}/customers/lookup.json?reference={userReference}");
            if (getResponse.IsSuccessStatusCode)
            {
                var content = await getResponse.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var customerResponse = JsonSerializer.Deserialize<CustomerResponse>(content, options);
                if (customerResponse?.Customer != null)
                {
                    return customerResponse.Customer;
                }
            }

            var createPayload = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userReference
                }
            };

            var json = JsonSerializer.Serialize(createPayload);
            var createRequest = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await _httpClient.PostAsync($"{_baseUrl}/customers.json", createRequest);
            createResponse.EnsureSuccessStatusCode();

            var createContent = await createResponse.Content.ReadAsStringAsync();
            var options2 = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var newCustomerResponse = JsonSerializer.Deserialize<CustomerResponse>(createContent, options2);

            if (newCustomerResponse?.Customer == null)
            {
                throw new InvalidOperationException("Failed to create or retrieve customer");
            }

            return newCustomerResponse.Customer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/retrieving customer from Maxio");
            throw;
        }
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string productHandle, int customerId, string userReference)
    {
        try
        {
            var payload = new
            {
                subscription = new
                {
                    product_handle = productHandle,
                    customer_id = customerId,
                    payment_collection_method = "automatic"
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var request = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/subscriptions.json", request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var subscriptionResponse = JsonSerializer.Deserialize<SubscriptionResponse>(content, options);

            if (subscriptionResponse?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription");
            }

            return subscriptionResponse.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio");
            throw;
        }
    }

    public async Task<List<SubscriptionDto>> GetCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/customers/{customerId}/subscriptions.json");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var subscriptionsResponse = JsonSerializer.Deserialize<SubscriptionsResponse>(content, options);

            return subscriptionsResponse?.Subscriptions ?? new List<SubscriptionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching customer subscriptions from Maxio");
            throw;
        }
    }
}

public class ProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyDto? ProductFamily { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;
}

public class ProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

internal class ProductsResponse
{
    [JsonPropertyName("products")]
    public List<ProductDto> Products { get; set; } = new();
}

internal class CustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

internal class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

internal class SubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
