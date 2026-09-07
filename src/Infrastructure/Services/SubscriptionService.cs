using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionService
{
    Task<List<ProductResponse>> ListProductsAsync();
    Task<SubscriptionResponse?> CreateSubscriptionAsync(string userId, string firstName, string lastName,
        string email, string productHandle);
    Task<List<SubscriptionResponse>> ListUserSubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(IMaxioClient maxioClient, MaxioSettings settings, ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<ProductResponse>> ListProductsAsync()
    {
        try
        {
            var response = await _maxioClient.GetAsync<ProductsListResponse>("/products.json");
            return response?.Items ?? new List<ProductResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing products");
            throw;
        }
    }

    public async Task<SubscriptionResponse?> CreateSubscriptionAsync(string userId, string firstName,
        string lastName, string email, string productHandle)
    {
        try
        {
            var payload = new CreateSubscriptionRequest
            {
                Subscription = new SubscriptionCreatePayload
                {
                    ProductHandle = productHandle,
                    CustomerAttributes = new CustomerAttributes
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                }
            };

            var response = await _maxioClient.PostAsync<SubscriptionCreateResponse>(
                "/subscriptions.json", payload);

            return response?.Subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {userId}", userId);
            throw;
        }
    }

    public async Task<List<SubscriptionResponse>> ListUserSubscriptionsAsync(string userId)
    {
        try
        {
            var response = await _maxioClient.GetAsync<SubscriptionsListResponse>(
                $"/subscriptions.json?customer_id[value]={userId}&customer_id[type]=reference");

            return response?.Subscriptions ?? new List<SubscriptionResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing subscriptions for user {userId}", userId);
            throw;
        }
    }
}

#region Response DTOs

public class ProductsListResponse
{
    [JsonPropertyName("products")]
    public List<ProductResponse> Items { get; set; } = new();
}

public class ProductResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
}

public class SubscriptionsListResponse
{
    [JsonPropertyName("subscriptions")]
    public List<SubscriptionResponse> Subscriptions { get; set; } = new();
}

public class SubscriptionCreateResponse
{
    [JsonPropertyName("subscription")]
    public SubscriptionResponse? Subscription { get; set; }
}

public class SubscriptionResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_id")]
    public int ProductId { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long? BalanceInCents { get; set; }
}

#endregion

#region Request DTOs

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public SubscriptionCreatePayload? Subscription { get; set; }
}

public class SubscriptionCreatePayload
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_attributes")]
    public CustomerAttributes? CustomerAttributes { get; set; }
}

public class CustomerAttributes
{
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

#endregion
