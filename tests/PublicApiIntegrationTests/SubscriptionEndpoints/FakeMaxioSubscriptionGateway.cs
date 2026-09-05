using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory stand-in for Maxio, substituted into the DI container by <see cref="SubscriptionApiFactory"/>
/// so PublicApi's subscription endpoints can be integration-tested without network access or real
/// Maxio sandbox credentials. Mirrors the idempotency contract of <c>MaxioSubscriptionGateway</c>:
/// subscribing twice to the same plan returns the same subscription rather than creating a duplicate.
/// </summary>
public class FakeMaxioSubscriptionGateway : IMaxioSubscriptionGateway
{
    public static readonly SubscriptionPlan ProPlan = new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceAmount = 299.00m,
        BillingIntervalCount = 1,
        BillingIntervalUnit = "month"
    };

    public static readonly SubscriptionPlan BasicPlan = new()
    {
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceAmount = 29.00m,
        BillingIntervalCount = 1,
        BillingIntervalUnit = "month"
    };

    private static long _nextSubscriptionId;

    private readonly ConcurrentDictionary<string, List<CustomerSubscription>> _subscriptionsByBuyerId = new();

    public int SubscribeCallCount { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { ProPlan, BasicPlan });

    public Task<CustomerSubscription> SubscribeAsync(string buyerId, string email, string planHandle, CancellationToken cancellationToken = default)
    {
        SubscribeCallCount++;

        var plan = new[] { ProPlan, BasicPlan }.FirstOrDefault(p => p.Handle == planHandle)
            ?? throw new MaxioApiException(HttpStatusCode.UnprocessableEntity, $"Product: not found ({planHandle})");

        var subscriptions = _subscriptionsByBuyerId.GetOrAdd(buyerId, _ => new List<CustomerSubscription>());
        lock (subscriptions)
        {
            var existing = subscriptions.FirstOrDefault(s => s.PlanHandle == planHandle);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var created = new CustomerSubscription
            {
                SubscriptionId = Interlocked.Increment(ref _nextSubscriptionId),
                PlanHandle = plan.Handle,
                PlanName = plan.Name,
                PriceAmount = plan.PriceAmount,
                State = "active",
                NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow
            };
            subscriptions.Add(created);
            return Task.FromResult(created);
        }
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var subscriptions = _subscriptionsByBuyerId.GetOrAdd(buyerId, _ => new List<CustomerSubscription>());
        lock (subscriptions)
        {
            return Task.FromResult<IReadOnlyList<CustomerSubscription>>(subscriptions.ToList());
        }
    }
}
