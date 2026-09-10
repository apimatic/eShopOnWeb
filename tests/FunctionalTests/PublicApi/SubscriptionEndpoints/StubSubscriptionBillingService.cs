using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.FunctionalTests.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A deterministic in-memory stand-in for the Maxio-backed billing service, so the endpoint tests
/// exercise routing, authentication and serialization without calling the live billing sandbox.
/// </summary>
public class StubSubscriptionBillingService : ISubscriptionBillingService
{
    public const string ProPlanHandle = "eshop-pro";

    public string? LastSubscriberReference { get; private set; }
    public string? LastPlanHandle { get; private set; }

    public Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SubscriptionPlan> plans = new List<SubscriptionPlan>
        {
            new() { Handle = ProPlanHandle, Name = "Pro Plan", PriceInCents = 29900, Interval = 1, IntervalUnit = "month" },
            new() { Handle = "basic-plan", Name = "Basic Plan", PriceInCents = 2900, Interval = 1, IntervalUnit = "month" },
        };
        return Task.FromResult(plans);
    }

    public Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        LastSubscriberReference = subscriber.Reference;
        LastPlanHandle = planHandle;

        if (planHandle != ProPlanHandle && planHandle != "basic-plan")
        {
            throw new SubscriptionPlanNotFoundException(planHandle);
        }

        return Task.FromResult(new CustomerSubscription
        {
            Id = 12345,
            State = "active",
            PlanHandle = planHandle,
            PlanName = "Pro Plan",
            PriceInCents = 29900,
            Currency = "USD",
            NextBillingAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero),
            ActivatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        LastSubscriberReference = subscriber.Reference;

        IReadOnlyCollection<CustomerSubscription> subscriptions = new List<CustomerSubscription>
        {
            new()
            {
                Id = 12345,
                State = "active",
                PlanHandle = ProPlanHandle,
                PlanName = "Pro Plan",
                PriceInCents = 29900,
                Currency = "USD",
                NextBillingAt = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero),
            },
        };
        return Task.FromResult(subscriptions);
    }
}
