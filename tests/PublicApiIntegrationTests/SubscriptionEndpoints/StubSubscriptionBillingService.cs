using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Stands in for the Maxio-backed billing service so the endpoint tests exercise routing,
/// authorization, identity resolution, serialization and error mapping without calling a live
/// billing site. The provider itself is covered by the unit tests.
/// </summary>
public class StubSubscriptionBillingService : ISubscriptionBillingService
{
    public const string KnownPlanHandle = "test-pro";
    public const string UnavailablePlanHandle = "billing-down";

    private static readonly ConcurrentDictionary<string, SubscriberSubscription> Subscriptions = new();
    private static long _nextId = 1000;

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriptionPlan> plans = new[]
        {
            new SubscriptionPlan
            {
                Handle = KnownPlanHandle,
                Name = "Test Pro",
                PriceInCents = 29900,
                Currency = "USD",
                Interval = 1,
                IntervalUnit = "month"
            }
        };

        return Task.FromResult(plans);
    }

    public Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string? planHandle,
        CancellationToken cancellationToken = default)
    {
        if (planHandle == UnavailablePlanHandle)
        {
            throw new SubscriptionBillingException(BillingErrorKind.Unavailable, "The billing system is unreachable.");
        }

        if (planHandle is not null && planHandle != KnownPlanHandle)
        {
            throw new SubscriptionBillingException(BillingErrorKind.NotFound, $"No subscription plan with handle '{planHandle}' exists.");
        }

        var key = subscriber.UserName + "|" + KnownPlanHandle;

        if (Subscriptions.TryGetValue(key, out var existing))
        {
            return Task.FromResult(SubscribeResult.AlreadySubscribed(existing));
        }

        var created = new SubscriberSubscription
        {
            Id = Interlocked.Increment(ref _nextId),
            State = "active",
            PlanHandle = KnownPlanHandle,
            PlanName = "Test Pro",
            PriceInCents = 29900,
            Currency = "USD",
            Interval = 1,
            IntervalUnit = "month",
            NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1),
            CreatedAt = DateTimeOffset.UtcNow,
            CustomerReference = "eshoponweb-" + subscriber.UserName
        };

        var stored = Subscriptions.GetOrAdd(key, created);

        return Task.FromResult(ReferenceEquals(stored, created)
            ? SubscribeResult.NewlyCreated(stored)
            : SubscribeResult.AlreadySubscribed(stored));
    }

    public Task<IReadOnlyList<SubscriberSubscription>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SubscriberSubscription> mine = Subscriptions
            .Where(pair => pair.Key.StartsWith(subscriber.UserName + "|", StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .OrderByDescending(s => s.CreatedAt)
            .ToArray();

        return Task.FromResult(mine);
    }
}
