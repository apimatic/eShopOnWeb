using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioSubscriptionService
{
    Task<List<ProductDto>> GetAvailablePlansAsync();
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string productHandle);
    Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(IMaxioApiClient apiClient, IOptions<MaxioSettings> options, ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<List<ProductDto>> GetAvailablePlansAsync()
    {
        _logger.LogInformation("Fetching available plans");
        var response = await _apiClient.GetAsync<ProductListResponse>("/products.json");
        return response?.Products ?? new List<ProductDto>();
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string userEmail, string firstName, string lastName, string productHandle)
    {
        _logger.LogInformation($"Creating subscription for user {userId} on product {productHandle}");

        var customer = await EnsureCustomerExistsAsync(userId, userEmail, firstName, lastName);

        var subscription = new CreateSubscriptionRequest
        {
            Subscription = new SubscriptionInput
            {
                CustomerId = customer.Id,
                ProductHandle = productHandle,
                PaymentCollectionMethod = "automatic"
            }
        };

        var response = await _apiClient.PostAsync<SubscriptionResponse>("/subscriptions.json", subscription);
        if (response?.Subscription == null)
            throw new InvalidOperationException("Failed to create subscription");

        _logger.LogInformation($"Subscription created: {response.Subscription.Id}");
        return response.Subscription;
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        _logger.LogInformation($"Fetching subscriptions for user {userId}");

        var customer = await GetCustomerByReferenceAsync(userId);
        if (customer == null)
        {
            _logger.LogWarning($"Customer not found for user {userId}");
            return new List<SubscriptionDto>();
        }

        var response = await _apiClient.GetAsync<SubscriptionListResponse>($"/customers/{customer.Id}/subscriptions.json");
        return response?.Subscriptions ?? new List<SubscriptionDto>();
    }

    private async Task<CustomerDto> EnsureCustomerExistsAsync(string userId, string email, string firstName, string lastName)
    {
        var existingCustomer = await GetCustomerByReferenceAsync(userId);
        if (existingCustomer != null)
        {
            _logger.LogInformation($"Customer already exists: {existingCustomer.Id}");
            return existingCustomer;
        }

        _logger.LogInformation($"Creating new customer for user {userId}");
        var createRequest = new CreateCustomerRequest
        {
            Customer = new CustomerInput
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Reference = userId
            }
        };

        var response = await _apiClient.PostAsync<CustomerResponse>("/customers.json", createRequest);
        if (response?.Customer == null)
            throw new InvalidOperationException("Failed to create customer");

        _logger.LogInformation($"Customer created: {response.Customer.Id}");
        return response.Customer;
    }

    private async Task<CustomerDto?> GetCustomerByReferenceAsync(string reference)
    {
        try
        {
            var response = await _apiClient.GetAsync<CustomerResponse>($"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}");
            return response?.Customer;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            return null;
        }
    }
}

#region DTOs

public class ProductListResponse
{
    [JsonPropertyName("products")]
    public List<ProductDto> Products { get; set; } = new();
}

public class ProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = "";

    [JsonPropertyName("product_family")]
    public ProductFamilyDto? ProductFamily { get; set; }
}

public class ProductFamilyDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }
}

public class CustomerDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";
}

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CustomerInput Customer { get; set; } = new();
}

public class CustomerInput
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionInput Subscription { get; set; } = new();
}

public class SubscriptionInput
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = "";

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "automatic";
}

#endregion
