using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operation is illegal from the subscription's current state — an unsupported
/// lifecycle transition (UC4) or a plan change on a subscription that is no longer live (UC3).
/// Carries the current state and the transitions that <em>are</em> legal, so the caller can tell
/// the customer what to do instead. No provider call is made when this is thrown.
/// </summary>
public class SubscriptionStateException : Exception
{
    /// <summary>The state the subscription was actually in.</summary>
    public SubscriptionState CurrentState { get; }

    /// <summary>The lifecycle transitions that are legal from <see cref="CurrentState"/>.</summary>
    public IReadOnlyCollection<SubscriptionLifecycleAction> AllowedTransitions { get; }

    public SubscriptionStateException(
        string requestedOperation,
        SubscriptionState currentState,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedTransitions)
        : base(BuildMessage(requestedOperation, currentState, allowedTransitions))
    {
        CurrentState = currentState;
        AllowedTransitions = allowedTransitions;
    }

    public SubscriptionStateException(
        SubscriptionLifecycleAction requestedAction,
        SubscriptionState currentState,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedTransitions)
        : this(requestedAction.ToString(), currentState, allowedTransitions)
    {
    }

    private static string BuildMessage(
        string requestedOperation,
        SubscriptionState currentState,
        IReadOnlyCollection<SubscriptionLifecycleAction> allowedTransitions)
    {
        var allowed = allowedTransitions.Any()
            ? string.Join(", ", allowedTransitions)
            : "none";

        return $"Cannot {requestedOperation} a subscription in state {currentState}. Allowed actions: {allowed}.";
    }
}
