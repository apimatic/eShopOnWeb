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
/// In-memory stand-in for the billing system so the endpoints can be exercised end to end without
/// calling Maxio. It reproduces the contract the real implementation guarantees: idempotent
/// subscribe, per-shopper isolation, and the billing exceptions the API translates to status codes.
/// </summary>
public class FakeSubscriptionService : ISubscriptionService
{
    public const string ProviderFailurePlanHandle = "provider-error";

    private readonly ConcurrentDictionary<string, List<CustomerSubscription>> _subscriptions = new();
    private long _nextId = 1000;

    public static readonly SubscriptionPlan ProPlan = new()
    {
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    public static readonly SubscriptionPlan BasicPlan = new()
    {
        Handle = "basic-plan",
        Name = "Basic Plan",
        PriceInCents = 2900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamilyHandle = "eshop-subscribe"
    };

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SubscriptionPlan>>(new[] { BasicPlan, ProPlan });

    public Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PlanHandle == ProviderFailurePlanHandle)
        {
            throw new BillingProviderException("Unable to create subscription in Maxio.", new[] { "upstream exploded" });
        }

        var plan = new[] { ProPlan, BasicPlan }
            .FirstOrDefault(candidate => string.Equals(candidate.Handle, request.PlanHandle, StringComparison.OrdinalIgnoreCase));

        if (plan is null)
        {
            throw new SubscriptionPlanNotFoundException(request.PlanHandle);
        }

        var forSubscriber = _subscriptions.GetOrAdd(request.Subscriber.Reference, _ => new List<CustomerSubscription>());

        lock (forSubscriber)
        {
            var existing = forSubscriber.FirstOrDefault(subscription =>
                subscription.IsLive && subscription.PlanHandle == plan.Handle);

            if (existing is not null)
            {
                return Task.FromResult(new SubscribeResult(existing, created: false));
            }

            var created = new CustomerSubscription
            {
                Id = Interlocked.Increment(ref _nextId),
                State = "active",
                PlanHandle = plan.Handle,
                PlanName = plan.Name,
                PlanPriceInCents = plan.PriceInCents,
                Interval = plan.Interval,
                IntervalUnit = plan.IntervalUnit,
                NextBillingAt = DateTimeOffset.UtcNow.AddMonths(1),
                CurrentPeriodStartedAt = DateTimeOffset.UtcNow,
                CurrentPeriodEndsAt = DateTimeOffset.UtcNow.AddMonths(1),
                CreatedAt = DateTimeOffset.UtcNow,
                ActivatedAt = DateTimeOffset.UtcNow,
                PaymentCollectionMethod = "remittance",
                Reference = request.IdempotencyKey is null ? null : $"eshoponweb-{request.IdempotencyKey}",
                CustomerId = 42,
                CustomerReference = request.Subscriber.Reference,
                CustomerEmail = request.Subscriber.Email
            };

            forSubscriber.Add(created);

            return Task.FromResult(new SubscribeResult(created, created: true));
        }
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        var forSubscriber = _subscriptions.TryGetValue(subscriber.Reference, out var subscriptions)
            ? subscriptions.OrderByDescending(subscription => subscription.CreatedAt).ToArray()
            : Array.Empty<CustomerSubscription>();

        return Task.FromResult<IReadOnlyList<CustomerSubscription>>(forSubscriber);
    }
}
