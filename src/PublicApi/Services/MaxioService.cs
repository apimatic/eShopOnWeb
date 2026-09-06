using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<MaxioProduct>> ListProductsAsync();
    Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName);
    Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId);
    Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MaxioService> _logger;
    private readonly string _baseUrl;
    private readonly string _authHeader;

    public MaxioService(MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _logger = logger;
        _baseUrl = settings.GetBaseUrl();

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        _authHeader = $"Basic {credentials}";

        var handler = new HttpClientHandler();
        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.Add("Authorization", _authHeader);
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
    }

    public async Task<List<MaxioProduct>> ListProductsAsync()
    {
        try
        {
            var url = $"{_baseUrl}/products.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<ProductsResponse>(content, options);

            return data?.Products ?? new List<MaxioProduct>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products from Maxio");
            throw;
        }
    }

    public async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        try
        {
            var url = $"{_baseUrl}/customers.json";
            var request = new
            {
                customer = new
                {
                    first_name = firstName,
                    last_name = lastName,
                    email = email,
                    reference = userId
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<CustomerResponse>(responseContent, options);

            return data?.Customer ?? throw new InvalidOperationException("Customer not found in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer in Maxio for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions.json";
            var request = new
            {
                subscription = new
                {
                    customer_id = customerId,
                    product_handle = productHandle,
                    auto_resume = true
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<SubscriptionResponse>(responseContent, options);

            return data?.Subscription ?? throw new InvalidOperationException("Subscription not found in response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription in Maxio for customerId: {CustomerId}, productHandle: {ProductHandle}", customerId, productHandle);
            throw;
        }
    }

    public async Task<MaxioSubscription?> GetSubscriptionAsync(long subscriptionId)
    {
        try
        {
            var url = $"{_baseUrl}/subscriptions/{subscriptionId}.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<SubscriptionResponse>(content, options);

            return data?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading subscription from Maxio with ID: {SubscriptionId}", subscriptionId);
            return null;
        }
    }

    public async Task<List<MaxioSubscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        try
        {
            var url = $"{_baseUrl}/customers/{customerId}/subscriptions.json";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<SubscriptionsResponse>(content, options);

            return data?.Subscriptions ?? new List<MaxioSubscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for customer {CustomerId}", customerId);
            throw;
        }
    }
}

// Response models
public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public int? PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

// Wrapper responses
public class ProductsResponse
{
    [JsonPropertyName("products")]
    public List<MaxioProduct>? Products { get; set; }
}

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class SubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription>? Subscriptions { get; set; }
}
