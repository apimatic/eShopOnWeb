using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

/// <summary>
/// Published after a lifecycle transition — pause, resume, cancel or reactivate (UC4 step 3).
/// </summary>
/// <remarks>Delivery is in-process and best-effort (plan.md §2.5).</remarks>
public class SubscriptionStateChanged : INotification
{
    public SubscriptionStateChanged(Subscription subscription,
        SubscriptionState previousState,
        string action)
    {
        Subscription = subscription;
        PreviousState = previousState;
        Action = action;
    }

    public Subscription Subscription { get; }

    public SubscriptionState PreviousState { get; }

    /// <summary>The state the subscription reached, as the provider reports it now.</summary>
    public SubscriptionState NewState => Subscription.State;

    /// <summary>The lifecycle action that was applied, e.g. "pause" or "cancel".</summary>
    public string Action { get; }
}
