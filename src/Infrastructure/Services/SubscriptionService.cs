using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionService
{
    Task<SubscriptionPlanDto> GetSubscriptionPlansAsync();
    Task<UserSubscriptionDto> CreateSubscriptionAsync(string userId, string email, string firstName, string lastName, string planHandle);
    Task<List<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IMaxioClient _maxioClient;
    private readonly IRepository<MaxioCustomer> _customerRepository;
    private readonly IRepository<UserSubscription> _subscriptionRepository;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        IMaxioClient maxioClient,
        IRepository<MaxioCustomer> customerRepository,
        IRepository<UserSubscription> subscriptionRepository,
        ILogger<SubscriptionService> logger)
    {
        _maxioClient = maxioClient;
        _customerRepository = customerRepository;
        _subscriptionRepository = subscriptionRepository;
        _logger = logger;
    }

    public async Task<SubscriptionPlanDto> GetSubscriptionPlansAsync()
    {
        try
        {
            var products = await _maxioClient.GetProductsAsync();
            var plans = products
                .Select(p => new SubscriptionPlanItemDto
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.GetPrice(),
                    BillingInterval = p.Interval,
                    BillingIntervalUnit = p.IntervalUnit
                })
                .ToList();

            return new SubscriptionPlanDto { Plans = plans };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription plans");
            throw;
        }
    }

    public async Task<UserSubscriptionDto> CreateSubscriptionAsync(string userId, string email, string firstName, string lastName, string planHandle)
    {
        try
        {
            var maxioCustomer = await GetOrCreateMaxioCustomerAsync(userId, email, firstName, lastName);

            var existing = await _subscriptionRepository.ListAsync(
                new SubscriptionByUserAndPlanSpecification(userId, planHandle));

            if (existing.Any(s => s.Status == "active"))
            {
                throw new InvalidOperationException($"User already has an active subscription to {planHandle}");
            }

            var subscription = await _maxioClient.CreateSubscriptionAsync(maxioCustomer.MaxioCustomerId, planHandle);

            var userSubscription = new UserSubscription
            {
                UserId = userId,
                MaxioSubscriptionId = subscription.Id,
                PlanHandle = planHandle,
                Status = subscription.State,
                ActivatedAt = subscription.ActivatedAt,
                NextBillingAt = subscription.NextBillingAt,
                CurrentPrice = subscription.ProductPrice
            };

            await _subscriptionRepository.AddAsync(userSubscription);

            return new UserSubscriptionDto
            {
                SubscriptionId = subscription.Id,
                PlanHandle = planHandle,
                Status = subscription.State,
                ActivatedAt = subscription.ActivatedAt,
                NextBillingAt = subscription.NextBillingAt,
                Price = subscription.ProductPrice
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create subscription for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        try
        {
            var userSubscriptions = await _subscriptionRepository.ListAsync(
                new SubscriptionByUserSpecification(userId));

            return userSubscriptions
                .Select(s => new UserSubscriptionDto
                {
                    SubscriptionId = s.MaxioSubscriptionId,
                    PlanHandle = s.PlanHandle,
                    Status = s.Status,
                    ActivatedAt = s.ActivatedAt,
                    NextBillingAt = s.NextBillingAt,
                    Price = s.CurrentPrice
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions for user {UserId}", userId);
            throw;
        }
    }

    private async Task<MaxioCustomer> GetOrCreateMaxioCustomerAsync(string userId, string email, string firstName, string lastName)
    {
        var existing = await _customerRepository.ListAsync(
            new MaxioCustomerByUserSpecification(userId));

        if (existing.Any())
        {
            return existing.First();
        }

        var customerDto = await _maxioClient.GetOrCreateCustomerAsync(userId, email, firstName, lastName);

        var maxioCustomer = new MaxioCustomer
        {
            UserId = userId,
            MaxioCustomerId = customerDto.Id,
            Email = customerDto.Email,
            FirstName = customerDto.FirstName,
            LastName = customerDto.LastName
        };

        await _customerRepository.AddAsync(maxioCustomer);
        return maxioCustomer;
    }
}

public class SubscriptionPlanDto
{
    public List<SubscriptionPlanItemDto> Plans { get; set; } = new();
}

public class SubscriptionPlanItemDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
}

public class UserSubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal Price { get; set; }
}

public class SubscriptionByUserSpecification : Ardalis.Specification.Specification<UserSubscription>
{
    public SubscriptionByUserSpecification(string userId)
    {
        Query.Where(s => s.UserId == userId);
    }
}

public class SubscriptionByUserAndPlanSpecification : Ardalis.Specification.Specification<UserSubscription>
{
    public SubscriptionByUserAndPlanSpecification(string userId, string planHandle)
    {
        Query.Where(s => s.UserId == userId && s.PlanHandle == planHandle);
    }
}

public class MaxioCustomerByUserSpecification : Ardalis.Specification.Specification<MaxioCustomer>
{
    public MaxioCustomerByUserSpecification(string userId)
    {
        Query.Where(c => c.UserId == userId);
    }
}
