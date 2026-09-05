using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioHttpClient _httpClient;
    private readonly MaxioConfiguration _config;
    private readonly ILogger<MaxioSubscriptionService> _logger;

    public MaxioSubscriptionService(MaxioHttpClient httpClient, MaxioConfiguration config, ILogger<MaxioSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<MaxioSubscriptionPlan[]> GetSubscriptionPlansAsync()
    {
        try
        {
            var endpoint = $"/product_families/handle:{_config.ProductFamilyHandle}/products.json";
            var response = await _httpClient.GetAsync<MaxioProductsResponse>(endpoint);

            if (response?.Products == null || response.Products.Count == 0)
            {
                _logger.LogWarning("No products found for product family {Family}", _config.ProductFamilyHandle);
                return Array.Empty<MaxioSubscriptionPlan>();
            }

            var plans = response.Products.Select(p => new MaxioSubscriptionPlan
            {
                Handle = p.Handle ?? p.Id.ToString(),
                Name = p.Name,
                PricePerMonth = ConvertCentsToDecimal(p.PriceInCents),
                Description = p.Description ?? ""
            }).ToArray();

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscription plans from Maxio");
            throw;
        }
    }

    public async Task<MaxioSubscription> CreateSubscriptionAsync(string userId, string userEmail, string userFirstName, string userLastName, string planHandle)
    {
        try
        {
            var customerRequest = new MaxioCustomerRequest
            {
                Customer = new MaxioCustomerRequest.CustomerData
                {
                    FirstName = userFirstName,
                    LastName = userLastName,
                    Email = userEmail,
                    Reference = userId
                }
            };

            var customerResponse = await _httpClient.PostAsync<MaxioCustomerResponse>("/customers.json", customerRequest);

            if (customerResponse?.Customer == null)
            {
                throw new InvalidOperationException("Failed to create or retrieve customer from Maxio");
            }

            var customerId = customerResponse.Customer.Id;
            _logger.LogInformation("Maxio customer created/retrieved: {CustomerId} for user {UserId}", customerId, userId);

            var subscriptionRequest = new MaxioSubscriptionRequest
            {
                Subscription = new MaxioSubscriptionRequest.SubscriptionData
                {
                    ProductHandle = planHandle,
                    CustomerId = customerId,
                    PaymentCollectionMethod = "automatic"
                }
            };

            var subscriptionResponse = await _httpClient.PostAsync<MaxioSubscriptionResponse>("/subscriptions.json", subscriptionRequest);

            if (subscriptionResponse?.Subscription == null)
            {
                throw new InvalidOperationException("Failed to create subscription in Maxio");
            }

            var subscription = subscriptionResponse.Subscription;
            _logger.LogInformation("Subscription created: {SubscriptionId} for customer {CustomerId}", subscription.Id, customerId);

            return new MaxioSubscription
            {
                Id = subscription.Id,
                State = subscription.State,
                ProductName = subscription.Product?.Name ?? "Unknown",
                ProductHandle = subscription.Product?.Handle ?? planHandle,
                PricePerMonth = ConvertCentsToDecimal(subscription.ProductPriceInCents),
                CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt ?? DateTime.UtcNow,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt ?? DateTime.UtcNow.AddMonths(1),
                NextAssessmentAt = subscription.NextAssessmentAt ?? DateTime.UtcNow.AddMonths(1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription for user {UserId} on plan {Plan}", userId, planHandle);
            throw;
        }
    }

    public async Task<MaxioSubscription[]> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var endpoint = $"/subscriptions.json?customer_reference={Uri.EscapeDataString(userId)}";
            var response = await _httpClient.GetAsync<MaxioSubscriptionsListResponse>(endpoint);

            if (response?.Subscriptions == null || response.Subscriptions.Count == 0)
            {
                return Array.Empty<MaxioSubscription>();
            }

            var subscriptions = response.Subscriptions.Select(s => new MaxioSubscription
            {
                Id = s.Id,
                State = s.State,
                ProductName = s.Product?.Name ?? "Unknown",
                ProductHandle = s.Product?.Handle ?? "unknown",
                PricePerMonth = ConvertCentsToDecimal(s.ProductPriceInCents),
                CurrentPeriodStartsAt = s.CurrentPeriodStartsAt ?? DateTime.UtcNow,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt ?? DateTime.UtcNow.AddMonths(1),
                NextAssessmentAt = s.NextAssessmentAt ?? DateTime.UtcNow.AddMonths(1)
            }).ToArray();

            return subscriptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private static decimal ConvertCentsToDecimal(long cents)
    {
        return cents / 100m;
    }
}

public class MaxioProductsResponse
{
    public List<MaxioProduct> Products { get; set; } = new();
}

public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
}
