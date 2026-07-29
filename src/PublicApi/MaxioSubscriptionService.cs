using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSubscriptionService
{
    private readonly MaxioApiClient _apiClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioApiClient apiClient, MaxioConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _apiClient = apiClient;
        _config = config;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetAvailablePlansAsync()
    {
        var productFamilyHandle = _config.ProductFamilyHandle;
        var path = $"/product_families/handle:{productFamilyHandle}/products.json";

        var response = await _apiClient.GetAsync<MaxioProductListResponse>(path);
        if (response?.Items == null)
            return new List<SubscriptionPlanDto>();

        return response.Items
            .Where(p => p.Product != null)
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Product!.Id,
                Handle = p.Product.Handle ?? string.Empty,
                Name = p.Product.Name ?? string.Empty,
                Description = p.Product.Description,
                PriceInCents = p.Product.PriceInCents,
                PriceFormatted = FormatPrice(p.Product.PriceInCents),
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.IntervalUnit ?? "month"
            })
            .ToList();
    }

    public async Task<SubscriptionDto?> CreateSubscriptionAsync(string userId, string userEmail, string userName, string planHandle)
    {
        // Ensure customer exists (idempotent)
        var customer = await EnsureCustomerExistsAsync(userId, userEmail, userName);
        if (customer == null)
            return null;

        // Check if user already has a subscription for this plan
        var existingSubscriptions = await GetCustomerSubscriptionsAsync(customer.Id);
        var existingPlanSubscription = existingSubscriptions?.FirstOrDefault(s =>
            s.Product?.Handle == planHandle && (s.State == "active" || s.State == "trialing"));

        if (existingPlanSubscription != null)
        {
            _logger.LogInformation("User {UserId} already has active subscription for plan {Plan}", userId, planHandle);
            return MapSubscriptionToDto(existingPlanSubscription);
        }

        // Create new subscription
        var createRequest = new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateSubscriptionData
            {
                CustomerId = customer.Id,
                ProductHandle = planHandle
            }
        };

        var response = await _apiClient.PostAsync<MaxioSubscriptionResponse>("/subscriptions.json", createRequest);
        if (response?.Subscription == null)
        {
            _logger.LogError("Failed to create subscription for user {UserId} on plan {Plan}", userId, planHandle);
            return null;
        }

        _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} on plan {Plan}",
            response.Subscription.Id, userId, planHandle);

        return MapSubscriptionToDto(response.Subscription);
    }

    public async Task<List<SubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        var customer = await GetCustomerByReferenceAsync(userId);
        if (customer == null)
            return new List<SubscriptionDto>();

        var subscriptions = await GetCustomerSubscriptionsAsync(customer.Id);
        return subscriptions?.Select(MapSubscriptionToDto).ToList() ?? new List<SubscriptionDto>();
    }

    private async Task<MaxioCustomer?> EnsureCustomerExistsAsync(string userId, string userEmail, string userName)
    {
        // Try to get existing customer by reference
        var existing = await GetCustomerByReferenceAsync(userId);
        if (existing != null)
            return existing;

        // Create new customer
        var names = userName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = names.Length > 0 ? names[0] : "User";
        var lastName = names.Length > 1 ? names[1] : userId;

        var createRequest = new { customer = new CreateCustomerData
        {
            FirstName = firstName,
            LastName = lastName,
            Email = userEmail,
            Reference = userId
        }};

        var response = await _apiClient.PostAsync<MaxioCustomerResponse>("/customers.json", createRequest);
        if (response?.Customer == null)
        {
            _logger.LogError("Failed to create customer for user {UserId}", userId);
            return null;
        }

        _logger.LogInformation("Created Maxio customer {CustomerId} for user {UserId}", response.Customer.Id, userId);
        return response.Customer;
    }

    private async Task<MaxioCustomer?> GetCustomerByReferenceAsync(string reference)
    {
        var path = $"/customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
        var response = await _apiClient.GetAsync<MaxioCustomerResponse>(path);
        return response?.Customer;
    }

    private async Task<List<MaxioSubscription>?> GetCustomerSubscriptionsAsync(int customerId)
    {
        var path = $"/customers/{customerId}/subscriptions.json";
        var response = await _apiClient.GetAsync<MaxioSubscriptionListResponse>(path);
        return response?.Subscriptions;
    }

    private SubscriptionDto MapSubscriptionToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            CustomerId = subscription.CustomerId,
            ProductName = subscription.Product?.Name ?? "Unknown",
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            State = subscription.State ?? "unknown",
            CreatedAt = subscription.CreatedAt,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
        };
    }

    private string FormatPrice(long cents)
    {
        return (cents / 100m).ToString("C");
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
}
