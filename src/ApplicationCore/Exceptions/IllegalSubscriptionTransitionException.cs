using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle transition is not legal from the subscription's current state (UC4).
/// No provider call is made when this is thrown.
/// </summary>
public class IllegalSubscriptionTransitionException : Exception
{
    public IllegalSubscriptionTransitionException(string currentState, string requestedAction, IEnumerable<string> legalActions)
        : base($"Cannot '{requestedAction}' a subscription in state '{currentState}'. Legal actions are: {string.Join(", ", legalActions)}.")
    {
        CurrentState = currentState;
        RequestedAction = requestedAction;
        LegalActions = legalActions.ToList();
    }

    public string CurrentState { get; }
    public string RequestedAction { get; }
    public IReadOnlyCollection<string> LegalActions { get; }
}
