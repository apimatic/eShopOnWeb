using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A lifecycle action was requested that is not legal from the subscription's current state
/// (UC4). No provider call is made when this is thrown.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId,
        string action,
        SubscriptionState currentState,
        IEnumerable<string> legalActions)
        : this(subscriptionId, action, currentState, legalActions,
            BuildMessage(action, currentState, legalActions))
    {
    }

    /// <summary>
    /// Rejects an action with wording of the caller's choosing, for cases the "cannot {action} a
    /// subscription that is {state}" phrasing does not fit — a no-op plan change, for instance,
    /// has nothing to do with the subscription's state.
    /// </summary>
    public InvalidSubscriptionTransitionException(int subscriptionId,
        string action,
        SubscriptionState currentState,
        IEnumerable<string> legalActions,
        string message)
        : base(message)
    {
        SubscriptionId = subscriptionId;
        Action = action;
        CurrentState = currentState;
        LegalActions = legalActions.ToArray();
    }

    public int SubscriptionId { get; }

    /// <summary>The action that was rejected, e.g. "resume".</summary>
    public string Action { get; }

    public SubscriptionState CurrentState { get; }

    /// <summary>The actions that would be legal from <see cref="CurrentState"/>.</summary>
    public IReadOnlyList<string> LegalActions { get; }

    private static string BuildMessage(string action, SubscriptionState currentState, IEnumerable<string> legalActions)
    {
        var legal = legalActions.ToArray();
        var allowed = legal.Length > 0
            ? $"Allowed from this state: {string.Join(", ", legal)}."
            : "No lifecycle actions are available from this state.";

        return $"Cannot {action} a subscription that is {currentState}. {allowed}";
    }
}
