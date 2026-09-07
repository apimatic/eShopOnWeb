using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionManager
{
    Task<IEnumerable<SubscriptionPlanDto>> GetSubscriptionPlansAsync();
    Task<SubscriptionCreatedDto> CreateSubscriptionAsync(string planHandle, string userId, string userEmail, string userFirstName, string userLastName);
    Task<IEnumerable<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId);
}

public class SubscriptionManager : ISubscriptionManager
{
    private readonly IMaxioService _maxioService;
    private readonly IRepository<Subscription> _subscriptionRepository;

    public SubscriptionManager(IMaxioService maxioService, IRepository<Subscription> subscriptionRepository)
    {
        _maxioService = maxioService;
        _subscriptionRepository = subscriptionRepository;
    }

    public async Task<IEnumerable<SubscriptionPlanDto>> GetSubscriptionPlansAsync()
    {
        var products = await _maxioService.GetProductsAsync();
        return products.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            Handle = p.Handle,
            Description = p.Description,
            PricePerMonth = p.PriceInCents / 100m,
            Interval = p.Interval,
            IntervalUnit = p.IntervalUnit,
        });
    }

    public async Task<SubscriptionCreatedDto> CreateSubscriptionAsync(string planHandle, string userId, string userEmail, string userFirstName, string userLastName)
    {
        var maxioSubscription = await _maxioService.GetOrCreateCustomerAndSubscribeAsync(
            userId,
            userFirstName,
            userLastName,
            userEmail,
            planHandle);

        var dbSubscription = new Subscription
        {
            UserId = userId,
            MaxioSubscriptionId = maxioSubscription.Id,
            MaxioCustomerId = maxioSubscription.CustomerId,
            PlanHandle = maxioSubscription.ProductHandle ?? planHandle,
            State = maxioSubscription.State,
            PriceInCents = maxioSubscription.PriceInCents,
            NextBillingAt = maxioSubscription.NextBillingAt,
            CreatedAt = maxioSubscription.CreatedAt,
            UpdatedAt = maxioSubscription.UpdatedAt,
        };

        await _subscriptionRepository.AddAsync(dbSubscription);

        return new SubscriptionCreatedDto
        {
            SubscriptionId = maxioSubscription.Id,
            CustomerId = maxioSubscription.CustomerId,
            PlanHandle = maxioSubscription.ProductHandle ?? planHandle,
            State = maxioSubscription.State,
            PricePerMonth = maxioSubscription.PriceInCents / 100m,
            NextBillingAt = maxioSubscription.NextBillingAt,
            Message = $"Successfully subscribed to {planHandle}"
        };
    }

    public async Task<IEnumerable<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId)
    {
        var customerReference = $"eshop-{userId}";
        var subscriptions = await _maxioService.GetCustomerSubscriptionsAsync(customerReference);

        return subscriptions.Select(s => new UserSubscriptionDto
        {
            SubscriptionId = s.Id,
            CustomerId = s.CustomerId,
            PlanHandle = s.ProductHandle,
            State = s.State,
            PricePerMonth = s.PriceInCents / 100m,
            NextBillingAt = s.NextBillingAt,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
        });
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Handle { get; set; }
    public string Description { get; set; }
    public decimal PricePerMonth { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
}

public class SubscriptionCreatedDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string Message { get; set; }
}

public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string PlanHandle { get; set; }
    public string State { get; set; }
    public decimal PricePerMonth { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
