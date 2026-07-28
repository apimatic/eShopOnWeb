using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// In-memory <see cref="ISubscriptionBillingService"/> that replaces the live Maxio adapter in tests,
/// so the HTTP surface (routing, auth, identity derivation, DTO mapping) is exercised without a network
/// call. Records the arguments the endpoints pass through.
/// </summary>
internal class StubBillingService : ISubscriptionBillingService
{
    public List<SubscriptionPlan> Plans { get; set; } = new();
    public CustomerSubscription? SubscribeResult { get; set; }
    public List<CustomerSubscription> Subscriptions { get; set; } = new();

    public SubscriberIdentity? LastSubscriber { get; private set; }
    public string? LastPlanHandle { get; private set; }

    public Task<IReadOnlyCollection<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<SubscriptionPlan>>(Plans);

    public Task<CustomerSubscription> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        LastSubscriber = subscriber;
        LastPlanHandle = planHandle;
        return Task.FromResult(SubscribeResult!);
    }

    public Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default)
    {
        LastSubscriber = subscriber;
        return Task.FromResult<IReadOnlyCollection<CustomerSubscription>>(Subscriptions);
    }
}
