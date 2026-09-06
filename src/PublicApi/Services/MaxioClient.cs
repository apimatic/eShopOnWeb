using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioClient
{
    Task<T?> GetAsync<T>(string endpoint) where T : class;
    Task<T?> PostAsync<T>(string endpoint, object body) where T : class;
    Task<Customer?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email);
    Task<List<Product>> GetProductsByFamilyHandleAsync(string familyHandle);
    Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle);
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId);
    Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId);
}

public class MaxioClient : IMaxioClient
{
    private readonly HttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public MaxioClient(HttpClient httpClient, IOptions<MaxioConfiguration> config, ILogger<MaxioClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var baseUrl = _config.GetBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl);

        if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.ApiKey}:X"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
        }
    }

    public async Task<T?> GetAsync<T>(string endpoint) where T : class
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio GET endpoint: {endpoint}", endpoint);
            return null;
        }
    }

    public async Task<T?> PostAsync<T>(string endpoint, object body) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(responseContent, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Maxio POST endpoint: {endpoint}", endpoint);
            throw;
        }
    }

    public async Task<Customer?> GetOrCreateCustomerAsync(string userId, string firstName, string lastName, string email)
    {
        var response = await GetAsync<CustomerResponse>($"/customers/lookup.json?reference={Uri.EscapeDataString(userId)}");

        if (response?.Customer != null)
        {
            _logger.LogInformation("Found existing Maxio customer for userId: {userId}", userId);
            return response.Customer;
        }

        _logger.LogInformation("Creating new Maxio customer for userId: {userId}", userId);
        var createRequest = new { customer = new { first_name = firstName, last_name = lastName, email = email, reference = userId } };
        var createResponse = await PostAsync<CustomerResponse>("/customers.json", createRequest);
        return createResponse?.Customer;
    }

    public async Task<List<Product>> GetProductsByFamilyHandleAsync(string familyHandle)
    {
        var response = await GetAsync<ProductListResponse>($"/products.json?include_archived=false");

        if (response?.Items == null)
            return new List<Product>();

        return response.Items
            .Where(p => p.Product?.ProductFamily?.Handle == familyHandle)
            .Select(p => p.Product)
            .Where(p => p != null)
            .ToList()!;
    }

    public async Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle)
    {
        var request = new
        {
            subscription = new
            {
                customer_reference = customerReference,
                product_handle = productHandle
            }
        };

        var response = await PostAsync<SubscriptionResponse>("/subscriptions.json", request);
        if (response?.Subscription == null)
            throw new InvalidOperationException("Failed to create subscription");

        return response.Subscription;
    }

    public async Task<Subscription?> GetSubscriptionAsync(int subscriptionId)
    {
        var response = await GetAsync<SubscriptionResponse>($"/subscriptions/{subscriptionId}.json");
        return response?.Subscription;
    }

    public async Task<List<Subscription>> ListCustomerSubscriptionsAsync(int customerId)
    {
        var response = await GetAsync<SubscriptionsListResponse>($"/customers/{customerId}/subscriptions.json");
        return response?.Subscriptions ?? new List<Subscription>();
    }
}

// Maxio API Response/Request DTOs

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public Customer? Customer { get; set; }
}

public class Customer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

public class ProductListResponse
{
    [JsonPropertyName("items")]
    public List<ProductItem>? Items { get; set; }
}

public class ProductItem
{
    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

public class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; set; }
}

public class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public Subscription? Subscription { get; set; }
}

public class SubscriptionsListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<Subscription>? Subscriptions { get; set; }
}

public class Subscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("trial_ends_at")]
    public DateTime? TrialEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
