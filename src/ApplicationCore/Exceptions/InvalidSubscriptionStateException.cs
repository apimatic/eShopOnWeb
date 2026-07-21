using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a lifecycle transition (pause/resume/cancel/reactivate) or a plan change is not
/// legal from the subscription's current state, whether caught by eShopOnWeb's own local check
/// or reported back by the provider after the state drifted out-of-band.
/// </summary>
public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(int subscriptionId, string currentState, string requestedTransition)
        : base($"Subscription {subscriptionId} cannot transition to '{requestedTransition}' from its current state '{currentState}'")
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
    }

    public InvalidSubscriptionStateException(int subscriptionId, string currentState, string requestedTransition, Exception innerException)
        : base($"Subscription {subscriptionId} cannot transition to '{requestedTransition}' from its current state '{currentState}'", innerException)
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
    }

    public int SubscriptionId { get; }
    public string CurrentState { get; }
}
