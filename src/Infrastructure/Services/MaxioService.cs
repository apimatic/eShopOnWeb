using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioService
{
    Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userReference, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto[]> GetCustomerSubscriptionsAsync(int customerId);
}

public class MaxioService : IMaxioService
{
    private readonly IMaxioApiClient _apiClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioService> _logger;

    public MaxioService(IMaxioApiClient apiClient, MaxioConfiguration config, ILogger<MaxioService> logger)
    {
        _apiClient = apiClient;
        _config = config;
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync()
    {
        var familyHandle = _config.ProductFamilyHandle ?? "eshop-subscribe";
        var response = await _apiClient.GetAsync<ProductFamilyResponse>($"/product_families/handle:{familyHandle}/products.json");

        var plans = response?.Items?.Select(item => item.Product).Where(p => p != null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                Description = p.Description,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit
            }).ToArray() ?? Array.Empty<SubscriptionPlanDto>();

        _logger.LogInformation("Retrieved {Count} subscription plans", plans.Length);
        return plans;
    }

    public async Task<CustomerDto> GetOrCreateCustomerAsync(string userReference, string email, string firstName, string lastName)
    {
        try
        {
            var customers = await _apiClient.GetAsync<CustomerListResponse>($"/customers.json?q={Uri.EscapeDataString(userReference)}");
            var existing = customers?.Customers?.FirstOrDefault(c => c.Reference == userReference);
            if (existing != null)
            {
                _logger.LogInformation("Found existing customer with reference {Reference}", userReference);
                return new CustomerDto
                {
                    Id = existing.Id,
                    Reference = existing.Reference,
                    Email = existing.Email,
                    FirstName = existing.FirstName,
                    LastName = existing.LastName
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup customer by reference, will create new");
        }

        var createRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerPayload
            {
                Reference = userReference,
                Email = email,
                FirstName = firstName,
                LastName = lastName
            }
        };

        var response = await _apiClient.PostAsync<CustomerResponse>("/customers.json", createRequest);
        if (response?.Customer == null)
        {
            throw new InvalidOperationException("Failed to create customer");
        }

        _logger.LogInformation("Created customer {CustomerId} with reference {Reference}", response.Customer.Id, userReference);
        return new CustomerDto
        {
            Id = response.Customer.Id,
            Reference = response.Customer.Reference,
            Email = response.Customer.Email,
            FirstName = response.Customer.FirstName,
            LastName = response.Customer.LastName
        };
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle)
    {
        var createRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionPayload
            {
                CustomerId = customerId,
                ProductHandle = productHandle
            }
        };

        var response = await _apiClient.PostAsync<SubscriptionResponse>("/subscriptions.json", createRequest);
        if (response?.Subscription == null)
        {
            throw new InvalidOperationException("Failed to create subscription");
        }

        _logger.LogInformation("Created subscription {SubscriptionId} for customer {CustomerId}", response.Subscription.Id, customerId);
        return MapSubscription(response.Subscription);
    }

    public async Task<SubscriptionDto[]> GetCustomerSubscriptionsAsync(int customerId)
    {
        var response = await _apiClient.GetAsync<SubscriptionListResponse>($"/customers/{customerId}/subscriptions.json");
        var subscriptions = response?.Subscriptions?.Select(MapSubscription).ToArray() ?? Array.Empty<SubscriptionDto>();
        _logger.LogInformation("Retrieved {Count} subscriptions for customer {CustomerId}", subscriptions.Length, customerId);
        return subscriptions;
    }

    private static SubscriptionDto MapSubscription(SubscriptionPayload sub)
    {
        return new SubscriptionDto
        {
            Id = sub.Id,
            State = sub.State,
            ProductHandle = sub.ProductHandle,
            ProductName = sub.ProductName,
            PriceInCents = sub.PriceInCents,
            NextBillingAt = sub.NextBillingAt,
            CreatedAt = sub.CreatedAt
        };
    }
}

#region DTOs

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

#endregion

#region API Response Models

public class ProductFamilyResponse
{
    [JsonPropertyName("items")]
    public List<ProductItem>? Items { get; set; }
}

public class ProductItem
{
    [JsonPropertyName("product")]
    public ProductPayload? Product { get; set; }
}

public class ProductPayload
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
}

public class CustomerListResponse
{
    [JsonPropertyName("customers")]
    public List<CustomerPayload>? Customers { get; set; }
}

public class CustomerResponse
{
    [JsonPropertyName("customer")]
    public CustomerPayload? Customer { get; set; }
}

public class CustomerPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

public class SubscriptionListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionPayload>? Subscriptions { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionPayload? Subscription { get; set; }
}

public class SubscriptionPayload
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}

#endregion

#region Request Models

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerPayload? Customer { get; set; }
}

public class CreateCustomerPayload
{
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionPayload? Subscription { get; set; }
}

public class CreateSubscriptionPayload
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }
}

#endregion
