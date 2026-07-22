using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition, carrying the old and new state. Published in-process, best-effort,
/// only after the provider has applied the transition.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(SubscriptionLifecycleAction action,
        SubscriptionStatus previousStatus,
        CustomerSubscription subscription)
    {
        Action = action;
        PreviousStatus = previousStatus;
        Subscription = subscription;
    }

    public SubscriptionLifecycleAction Action { get; }

    public SubscriptionStatus PreviousStatus { get; }

    public CustomerSubscription Subscription { get; }

    public SubscriptionStatus NewStatus => Subscription.Status;
}
