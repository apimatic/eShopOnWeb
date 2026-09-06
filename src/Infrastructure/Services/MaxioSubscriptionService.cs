using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface IMaxioSubscriptionService
{
    Task<GetSubscriptionPlansResponse> GetSubscriptionPlansAsync();
    Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userName, string productHandle);
    Task<GetSubscriptionsResponse> GetUserSubscriptionsAsync(string userName);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly IMaxioApiClient _apiClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly MaxioSettings _settings;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(
        IMaxioApiClient apiClient,
        UserManager<ApplicationUser> userManager,
        MaxioSettings settings,
        ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _userManager = userManager;
        _settings = settings;
        _logger = logger;
    }

    public async Task<GetSubscriptionPlansResponse> GetSubscriptionPlansAsync()
    {
        var response = await _apiClient.GetAsync<List<ProductWrapper>>("/products.json");
        if (response == null || response.Count == 0)
        {
            return new GetSubscriptionPlansResponse { Plans = new List<SubscriptionPlanDto>() };
        }

        var plans = new List<SubscriptionPlanDto>();
        foreach (var wrapper in response)
        {
            var product = wrapper.Product;
            if (product != null && string.Equals(product.ProductFamily?.Handle, _settings.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
            {
                plans.Add(new SubscriptionPlanDto
                {
                    Id = product.Id,
                    Handle = product.Handle,
                    Name = product.Name,
                    Description = product.Description,
                    Price = product.DefaultPrice ?? "0.00",
                    Interval = product.Interval ?? 1,
                    IntervalUnit = product.IntervalUnit ?? "month"
                });
            }
        }

        return new GetSubscriptionPlansResponse { Plans = plans };
    }

    public async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(string userName, string productHandle)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null)
        {
            throw new InvalidOperationException($"User {userName} not found");
        }

        var customerId = await EnsureMaxioCustomerAsync(user);

        var createSubRequest = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                CustomerId = int.Parse(customerId),
                ProductHandle = productHandle,
                CreditCardAttributes = new CreditCardAttributes
                {
                    FullNumber = "1",
                    ExpirationMonth = "12",
                    ExpirationYear = "2027"
                }
            }
        };

        try
        {
            var response = await _apiClient.PostAsync<CreateSubscriptionApiResponse>("/subscriptions.json", createSubRequest);
            if (response?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription in Maxio");
            }

            return new CreateSubscriptionResponse
            {
                SubscriptionId = response.Subscription.Id,
                Status = response.Subscription.State,
                NextBillingDate = response.Subscription.NextBillingAt,
                ProductName = response.Subscription.ProductName ?? $"Product (ID: {response.Subscription.ProductId})"
            };
        }
        catch (System.Net.Http.HttpRequestException ex) when (ex.Message.Contains("422"))
        {
            throw new InvalidOperationException("Failed to create subscription: Payment method may be required", ex);
        }
    }

    public async Task<GetSubscriptionsResponse> GetUserSubscriptionsAsync(string userName)
    {
        var user = await _userManager.FindByNameAsync(userName);
        if (user == null || string.IsNullOrEmpty(user.MaxioCustomerId))
        {
            return new GetSubscriptionsResponse { Subscriptions = new List<SubscriptionDto>() };
        }

        var response = await _apiClient.GetAsync<List<SubscriptionWrapper>>(
            $"/customers/{user.MaxioCustomerId}/subscriptions.json");

        if (response == null || response.Count == 0)
        {
            return new GetSubscriptionsResponse { Subscriptions = new List<SubscriptionDto>() };
        }

        var subscriptions = new List<SubscriptionDto>();
        foreach (var wrapper in response)
        {
            var sub = wrapper.Subscription;
            if (sub != null)
            {
                subscriptions.Add(new SubscriptionDto
                {
                    Id = sub.Id,
                    Status = sub.State,
                    ProductName = sub.ProductName ?? $"Product (ID: {sub.ProductId})",
                    NextBillingDate = sub.NextBillingAt,
                    CurrentPeriodStart = sub.CurrentPeriodStartsAt,
                    CurrentPeriodEnd = sub.CurrentPeriodEndsAt
                });
            }
        }

        return new GetSubscriptionsResponse { Subscriptions = subscriptions };
    }

    private async Task<string> EnsureMaxioCustomerAsync(ApplicationUser user)
    {
        if (!string.IsNullOrEmpty(user.MaxioCustomerId))
        {
            return user.MaxioCustomerId;
        }

        var createCustomerRequest = new CreateCustomerRequest
        {
            Customer = new CreateCustomerData
            {
                FirstName = user.UserName ?? "User",
                LastName = "User",
                Email = user.Email ?? "",
                Reference = user.Id
            }
        };

        var response = await _apiClient.PostAsync<CreateCustomerApiResponse>("/customers.json", createCustomerRequest);
        if (response?.Customer?.Id == null)
        {
            throw new InvalidOperationException("Failed to create customer in Maxio");
        }

        user.MaxioCustomerId = response.Customer.Id.ToString();
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to update user with Maxio customer ID");
        }

        return user.MaxioCustomerId ?? throw new InvalidOperationException("Failed to store Maxio customer ID");
    }
}

public class GetSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public DateTime? Price { get; set; }
}

public class GetSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
}

#region Maxio API Request/Response DTOs

public class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionData Subscription { get; set; } = new();
}

public class CreateSubscriptionData
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("credit_card_attributes")]
    public CreditCardAttributes? CreditCardAttributes { get; set; }
}

public class CreditCardAttributes
{
    [JsonPropertyName("full_number")]
    public string FullNumber { get; set; } = string.Empty;

    [JsonPropertyName("expiration_month")]
    public string ExpirationMonth { get; set; } = string.Empty;

    [JsonPropertyName("expiration_year")]
    public string ExpirationYear { get; set; } = string.Empty;
}

public class CreateSubscriptionApiResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_name")]
    public string? ProductName { get; set; }

    [JsonPropertyName("product_id")]
    public int? ProductId { get; set; }

    [JsonPropertyName("next_billing_at")]
    public DateTime? NextBillingAt { get; set; }

    [JsonPropertyName("current_period_starts_at")]
    public DateTime? CurrentPeriodStartsAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }
}

public class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomerData Customer { get; set; } = new();
}

public class CreateCustomerData
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

public class CreateCustomerApiResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

public class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }
}

public class SubscriptionWrapper
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class ProductWrapper
{
    [JsonPropertyName("product")]
    public Product? Product { get; set; }
}

public class Product
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("default_price")]
    public string? DefaultPrice { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamily? ProductFamily { get; set; }
}

public class ProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

#endregion
