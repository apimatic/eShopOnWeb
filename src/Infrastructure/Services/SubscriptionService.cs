using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public interface ISubscriptionService
{
    Task<Subscription?> GetSubscriptionByUserIdAndMaxioSubscriptionIdAsync(string userId, long maxioSubscriptionId);
    Task<Subscription?> CreateOrUpdateSubscriptionAsync(string userId, int maxioCustomerId, long maxioSubscriptionId,
        string productHandle, MaxioSubscriptionResponse subscription);
    Task<IEnumerable<Subscription>> GetUserSubscriptionsAsync(string userId);
    Task<IEnumerable<Subscription>> GetUserActiveSubscriptionsAsync(string userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly IRepository<Subscription> _subscriptionRepository;

    public SubscriptionService(IRepository<Subscription> subscriptionRepository)
    {
        _subscriptionRepository = subscriptionRepository;
    }

    public async Task<Subscription?> GetSubscriptionByUserIdAndMaxioSubscriptionIdAsync(string userId, long maxioSubscriptionId)
    {
        var subscriptions = await _subscriptionRepository.ListAsync();
        return subscriptions.FirstOrDefault(s => s.UserId == userId && s.MaxioSubscriptionId == maxioSubscriptionId);
    }

    public async Task<Subscription?> CreateOrUpdateSubscriptionAsync(string userId, int maxioCustomerId, long maxioSubscriptionId,
        string productHandle, MaxioSubscriptionResponse subscription)
    {
        var existing = await GetSubscriptionByUserIdAndMaxioSubscriptionIdAsync(userId, maxioSubscriptionId);

        if (existing != null)
        {
            existing.State = subscription.State;
            existing.NextAssessmentAt = subscription.NextAssessmentAt;
            existing.UpdatedAt = subscription.UpdatedAt;
            await _subscriptionRepository.UpdateAsync(existing);
            return existing;
        }

        var newSubscription = new Subscription
        {
            UserId = userId,
            MaxioCustomerId = maxioCustomerId,
            MaxioSubscriptionId = maxioSubscriptionId,
            ProductHandle = productHandle,
            State = subscription.State,
            ActivatedAt = subscription.ActivatedAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            PriceInCents = subscription.ProductPriceInCents,
            IntervalUnit = subscription.Product?.IntervalUnit ?? "month",
            Interval = subscription.Product?.Interval ?? 1,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt
        };

        await _subscriptionRepository.AddAsync(newSubscription);
        return newSubscription;
    }

    public async Task<IEnumerable<Subscription>> GetUserSubscriptionsAsync(string userId)
    {
        var subscriptions = await _subscriptionRepository.ListAsync();
        return subscriptions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt);
    }

    public async Task<IEnumerable<Subscription>> GetUserActiveSubscriptionsAsync(string userId)
    {
        var subscriptions = await GetUserSubscriptionsAsync(userId);
        return subscriptions.Where(s => s.State == "active");
    }
}
