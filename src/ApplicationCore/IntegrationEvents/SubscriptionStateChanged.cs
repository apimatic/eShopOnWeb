using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published in-process after a subscription lifecycle transition, carrying old to new state.
/// Delivery is best-effort.
/// </summary>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(string userReference, int subscriptionId,
        BillingSubscriptionState previousState, BillingSubscriptionState newState,
        SubscriptionLifecycleAction action)
    {
        UserReference = userReference;
        SubscriptionId = subscriptionId;
        PreviousState = previousState;
        NewState = newState;
        Action = action;
    }

    public string UserReference { get; }

    public int SubscriptionId { get; }

    public BillingSubscriptionState PreviousState { get; }

    public BillingSubscriptionState NewState { get; }

    public SubscriptionLifecycleAction Action { get; }
}
