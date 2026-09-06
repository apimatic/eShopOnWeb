using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlan>> GetAvailablePlansAsync();
    Task<SubscriptionResult> SubscribeUserAsync(string userId, string productHandle, string? productPricePointHandle = null);
    Task<List<UserSubscription>> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly IUserMaxioCustomerMappingStore _customerMappingStore;
    private readonly MaxioConfiguration _maxioConfig;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        IUserMaxioCustomerMappingStore customerMappingStore,
        MaxioConfiguration maxioConfig,
        UserManager<ApplicationUser> userManager,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _customerMappingStore = customerMappingStore;
        _maxioConfig = maxioConfig;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlansAsync()
    {
        try
        {
            var products = await _maxioClient.ListProductsAsync();
            var plans = new List<SubscriptionPlan>();

            foreach (var product in products)
            {
                if (product.ProductFamilyName.Equals(_maxioConfig.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase))
                {
                    plans.Add(new SubscriptionPlan
                    {
                        Handle = product.Handle,
                        Name = product.Name,
                        PriceInCents = 0,
                        Description = $"{product.ProductFamilyName} - {product.Name}"
                    });
                }
            }

            return plans;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available subscription plans");
            throw;
        }
    }

    public async Task<SubscriptionResult> SubscribeUserAsync(string userId, string productHandle, string? productPricePointHandle = null)
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException($"User {userId} not found");
            }

            var maxioCustomerId = await _customerMappingStore.GetMaxioCustomerIdAsync(userId);

            if (!maxioCustomerId.HasValue)
            {
                var customer = await _maxioClient.GetOrCreateCustomerAsync(
                    user.Email ?? "",
                    user.UserName ?? "",
                    user.UserName ?? "",
                    userId);

                if (customer == null)
                {
                    throw new InvalidOperationException("Failed to create Maxio customer");
                }

                maxioCustomerId = customer.Id;
                await _customerMappingStore.StoreAsync(userId, customer.Id);
                _logger.LogInformation("Created new Maxio customer {MaxioCustomerId} for user {UserId}", customer.Id, userId);
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(
                maxioCustomerId.Value,
                productHandle,
                productPricePointHandle);

            _logger.LogInformation("Created subscription {SubscriptionId} for user {UserId} to product {ProductHandle}",
                subscription.Id, userId, productHandle);

            return new SubscriptionResult
            {
                SubscriptionId = subscription.Id,
                CustomerId = subscription.CustomerId,
                ProductHandle = subscription.ProductHandle,
                ProductName = subscription.ProductName,
                State = subscription.State,
                CurrentPriceInCents = subscription.CurrentPriceInCents,
                NextBillingAt = subscription.NextBillingAt,
                ActivatedAt = subscription.ActivatedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe user {UserId} to product {ProductHandle}", userId, productHandle);
            throw;
        }
    }

    public async Task<List<UserSubscription>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var maxioCustomerId = await _customerMappingStore.GetMaxioCustomerIdAsync(userId);

            if (!maxioCustomerId.HasValue)
            {
                _logger.LogInformation("No Maxio customer found for user {UserId}", userId);
                return new List<UserSubscription>();
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(maxioCustomerId.Value);

            return subscriptions.Select(s => new UserSubscription
            {
                SubscriptionId = s.Id,
                ProductHandle = s.ProductHandle,
                ProductName = s.ProductName,
                State = s.State,
                CurrentPriceInCents = s.CurrentPriceInCents,
                NextBillingAt = s.NextBillingAt,
                ActivatedAt = s.ActivatedAt
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions for user {UserId}", userId);
            throw;
        }
    }
}

public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceInCents { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class SubscriptionResult
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CurrentPriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
}

public class UserSubscription
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal CurrentPriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
}
