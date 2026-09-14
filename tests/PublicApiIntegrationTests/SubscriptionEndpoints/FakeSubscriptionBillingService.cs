using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// A configurable fake of <see cref="ISubscriptionBillingService"/> so the subscription endpoints can
/// be tested (routing, auth, status codes, DTO mapping) without touching Maxio.
/// </summary>
public class FakeSubscriptionBillingService : ISubscriptionBillingService
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
    public List<CustomerSubscription> Subscriptions { get; set; } = new();
    public Func<SubscriberIdentity, string?, CustomerSubscription>? OnSubscribe { get; set; }
    public Exception? SubscribeException { get; set; }

    public SubscriberIdentity? LastSubscriber { get; private set; }
    public string? LastPlanHandle { get; private set; }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Plans);

    public Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken = default)
    {
        LastSubscriber = subscriber;
        LastPlanHandle = planHandle;
        if (SubscribeException is not null)
            throw SubscribeException;
        return Task.FromResult(OnSubscribe!(subscriber, planHandle));
    }

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string subscriberReference, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CustomerSubscription>>(Subscriptions);
}
