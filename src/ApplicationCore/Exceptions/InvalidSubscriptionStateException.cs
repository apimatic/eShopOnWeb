using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested operation is not legal from the subscription's current state — either caught by
/// eShopOnWeb's own pre-flight check (UC3/UC4 "illegal transition" failure scenarios), or by the
/// provider rejecting a transition after local state had drifted out-of-band (UC4: "treat the
/// provider's state as truth, refresh the local view, and surface the conflict").
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(int subscriptionId, SubscriptionState currentState, string attemptedAction)
        : base($"Subscription {subscriptionId} cannot {attemptedAction} while in state {currentState}.")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
    }

    public int SubscriptionId { get; }
    public SubscriptionState CurrentState { get; }
}
