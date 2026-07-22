using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle action is not legal from the subscription's current state.
/// Per UC4 no provider call is made in that case, and the caller is told which actions are legal.
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(string action, SubscriptionState currentState,
        IEnumerable<string> legalActions)
        : base($"Cannot {action} a subscription in state '{currentState}'. Legal actions: {string.Join(", ", legalActions)}.")
    {
        Action = action;
        CurrentState = currentState;
        LegalActions = legalActions.ToArray();
    }

    public string Action { get; }

    public SubscriptionState CurrentState { get; }

    public IReadOnlyCollection<string> LegalActions { get; }
}
