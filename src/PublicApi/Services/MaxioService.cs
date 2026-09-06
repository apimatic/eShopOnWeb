using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Services;

public interface IMaxioService
{
    Task<List<SubscriptionPlan>> GetPlansAsync();
    Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string userEmail, string userFirstName, string userLastName, string planHandle);
    Task<List<MaxioSubscription>> GetSubscriptionsForUserAsync(string userId);
}

public class MaxioService : IMaxioService
{
    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IMaxioApiClient apiClient, MaxioSettings settings, ILogger<MaxioService> logger)
    {
        _apiClient = apiClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlan>> GetPlansAsync()
    {
        _logger.LogInformation("Fetching subscription plans from product family {Handle}", _settings.ProductFamilyHandle);

        var response = await _apiClient.GetAsync<ProductsResponse>("products.json");

        var plans = response.Products
            .Where(p => p.ProductFamily?.Handle == _settings.ProductFamilyHandle)
            .Select(p => new SubscriptionPlan
            {
                Handle = p.Handle,
                Name = p.Name,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                IntervalUnit = p.IntervalUnit,
                Interval = p.Interval
            })
            .ToList();

        _logger.LogInformation("Found {Count} plans in family {Handle}", plans.Count, _settings.ProductFamilyHandle);
        return plans;
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string userEmail, string userFirstName, string userLastName, string planHandle)
    {
        _logger.LogInformation("Creating subscription for user {UserId} on plan {PlanHandle}", userId, planHandle);

        var customer = await GetOrCreateCustomerAsync(userId, userEmail, userFirstName, userLastName);
        _logger.LogInformation("Using Maxio customer {CustomerId}", customer.Id);

        var subscriptionRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionRequestData
            {
                ProductHandle = planHandle,
                CustomerId = customer.Id,
                Reference = userId
            }
        };

        var response = await _apiClient.PostAsync<CreateSubscriptionResponse>("subscriptions.json", subscriptionRequest);

        if (response.Subscription == null)
        {
            throw new InvalidOperationException("Failed to create subscription");
        }

        _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription.Id, customer.Id);

        return response.Subscription;
    }

    public async Task<List<MaxioSubscription>> GetSubscriptionsForUserAsync(string userId)
    {
        _logger.LogInformation("Fetching subscriptions for user {UserId}", userId);

        try
        {
            var customer = await _apiClient.GetAsync<CustomerResponse>($"customers/lookup.json?reference={userId}");

            if (customer?.Customer == null)
            {
                _logger.LogWarning("No customer found for user {UserId}", userId);
                return new List<MaxioSubscription>();
            }

            var response = await _apiClient.GetAsync<SubscriptionsResponse>($"subscriptions.json?customer_id={customer.Customer.Id}");
            _logger.LogInformation("Found {Count} subscriptions for customer {CustomerId}", response.Subscriptions.Count, customer.Customer.Id);

            return response.Subscriptions;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            _logger.LogInformation("No customer found for user {UserId}", userId);
            return new List<MaxioSubscription>();
        }
    }

    private async Task<MaxioCustomer> GetOrCreateCustomerAsync(string userId, string userEmail, string userFirstName, string userLastName)
    {
        try
        {
            var response = await _apiClient.GetAsync<CustomerResponse>($"customers/lookup.json?reference={userId}");
            if (response?.Customer != null)
            {
                _logger.LogInformation("Found existing customer {CustomerId} for user {UserId}", response.Customer.Id, userId);
                return response.Customer;
            }
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            _logger.LogInformation("Customer not found for user {UserId}, creating new one", userId);
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerRequestData
            {
                FirstName = userFirstName,
                LastName = userLastName,
                Email = userEmail,
                Reference = userId
            }
        };

        var createResponse = await _apiClient.PostAsync<CustomerResponse>("customers.json", createRequest);

        if (createResponse?.Customer == null)
        {
            throw new InvalidOperationException("Failed to create customer");
        }

        _logger.LogInformation("Created customer {CustomerId} for user {UserId}", createResponse.Customer.Id, userId);
        return createResponse.Customer;
    }
}

#region DTOs

public class SubscriptionPlan
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("priceInCents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("intervalUnit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public string? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public string? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
}

public class ProductsResponse
{
    [JsonPropertyName("products")]
    public List<Product> Products { get; set; } = new();
}

public class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; set; }
}

public class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class SubscriptionsResponse
{
    [JsonPropertyName("subscriptions")]
    public List<MaxioSubscription> Subscriptions { get; set; } = new();
}

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerRequestData Customer { get; set; } = new();
}

public class CreateCustomerRequestData
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionRequestData Subscription { get; set; } = new();
}

public class CreateSubscriptionRequestData
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

#endregion
