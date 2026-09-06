using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionService
{
    Task<SubscriptionPlan[]> GetPlansAsync();
    Task<UserSubscription?> SubscribeAsync(ApplicationUser user, string planHandle);
    Task<UserSubscription[]> GetUserSubscriptionsAsync(ApplicationUser user);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioApiClient _maxioClient;
    private readonly IRepository<MaxioCustomerMapping> _customerMappingRepo;
    private readonly IReadRepository<MaxioCustomerMapping> _customerMappingReadRepo;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly MaxioSettings _settings;

    public SubscriptionService(
        IMaxioApiClient maxioClient,
        IRepository<MaxioCustomerMapping> customerMappingRepo,
        IReadRepository<MaxioCustomerMapping> customerMappingReadRepo,
        ILogger<SubscriptionService> logger,
        MaxioSettings settings)
    {
        _maxioClient = maxioClient;
        _customerMappingRepo = customerMappingRepo;
        _customerMappingReadRepo = customerMappingReadRepo;
        _logger = logger;
        _settings = settings;
    }

    public async Task<SubscriptionPlan[]> GetPlansAsync()
    {
        try
        {
            var products = await _maxioClient.ListProductsAsync(_settings.ProductFamilyHandle);
            return products.Select(p => new SubscriptionPlan
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle,
                PriceInCents = p.PriceInCents,
                PriceUSD = p.PriceInCents / 100.0m,
                BillingUnit = p.IntervalUnit
            }).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching plans");
            throw;
        }
    }

    public async Task<UserSubscription?> SubscribeAsync(ApplicationUser user, string planHandle)
    {
        try
        {
            // Get or create Maxio customer
            var customerId = await EnsureMaxioCustomerAsync(user);
            if (customerId == 0)
            {
                _logger.LogError("Failed to get/create Maxio customer");
                return null;
            }

            // Create subscription
            var subscription = await _maxioClient.CreateSubscriptionAsync(customerId, planHandle);
            if (subscription == null)
            {
                _logger.LogError("Failed to create subscription");
                return null;
            }

            return new UserSubscription
            {
                Id = subscription.Id,
                MaxioCustomerId = customerId,
                UserId = user.Id,
                PlanHandle = subscription.ProductHandle,
                State = subscription.State,
                CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
                NextAssessmentAt = subscription.NextAssessmentAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing user");
            throw;
        }
    }

    public async Task<UserSubscription[]> GetUserSubscriptionsAsync(ApplicationUser user)
    {
        try
        {
            var spec = new MaxioCustomerByUserIdSpecification(user.Id);
            var mapping = await _customerMappingReadRepo.FirstOrDefaultAsync(spec);
            if (mapping == null)
            {
                return [];
            }

            var subscriptions = await _maxioClient.ListCustomerSubscriptionsAsync(mapping.MaxioCustomerId);
            return subscriptions.Select(s => new UserSubscription
            {
                Id = s.Id,
                MaxioCustomerId = mapping.MaxioCustomerId,
                UserId = user.Id,
                PlanHandle = s.ProductHandle,
                State = s.State,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt
            }).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user subscriptions");
            throw;
        }
    }

    private async Task<int> EnsureMaxioCustomerAsync(ApplicationUser user)
    {
        // Try to find existing mapping
        var spec = new MaxioCustomerByUserIdSpecification(user.Id);
        var mapping = await _customerMappingReadRepo.FirstOrDefaultAsync(spec);
        if (mapping != null)
        {
            return mapping.MaxioCustomerId;
        }

        // Create new customer in Maxio
        var reference = $"eshop-{user.Id}";
        var customerResponse = await _maxioClient.CreateOrGetCustomerAsync(
            reference,
            user.Email?.Split('@')[0] ?? "User",
            user.Email?.Split('@')[0] ?? "User",
            user.Email ?? string.Empty
        );

        if (customerResponse == null)
        {
            return 0;
        }

        // Store mapping
        var newMapping = new MaxioCustomerMapping
        {
            UserId = user.Id,
            MaxioCustomerId = customerResponse.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _customerMappingRepo.AddAsync(newMapping);

        return customerResponse.Id;
    }
}

public class SubscriptionPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal PriceUSD { get; set; }
    public string BillingUnit { get; set; } = string.Empty;
}

public class UserSubscription
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
}
