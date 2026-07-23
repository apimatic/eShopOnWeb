using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Announces a lifecycle transition (pause, resume, cancel, reactivate) carrying old to new
/// state. Published in-process, best-effort, only after the provider applied the transition.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference,
        BillingSubscription subscription,
        SubscriptionStatus previousStatus,
        string action)
    {
        UserReference = userReference;
        Subscription = subscription;
        PreviousStatus = previousStatus;
        Action = action;
    }

    public string UserReference { get; }

    public BillingSubscription Subscription { get; }

    public SubscriptionStatus PreviousStatus { get; }

    public SubscriptionStatus NewStatus => Subscription.Status;

    /// <summary>The lifecycle action that caused the transition, e.g. <c>pause</c> or <c>cancel</c>.</summary>
    public string Action { get; }
}
