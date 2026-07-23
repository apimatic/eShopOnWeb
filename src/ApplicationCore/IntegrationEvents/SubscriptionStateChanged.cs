using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a lifecycle transition has been applied (UC4, step 3), carrying the
/// old and new states. Delivery is best-effort: a failing handler never rolls back the transition.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        SubscriptionStatus previousStatus,
        CustomerSubscription subscription)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        Action = action;
        PreviousStatus = previousStatus;
        Subscription = subscription;
    }

    public string UserReference { get; }

    public int SubscriptionId { get; }

    public SubscriptionLifecycleAction Action { get; }

    public SubscriptionStatus PreviousStatus { get; }

    public SubscriptionStatus NewStatus => Subscription.Status;

    public CustomerSubscription Subscription { get; }
}
