using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a lifecycle transition is illegal from the subscription's current state. Thrown
/// before any provider call is made, so a rejected transition costs nothing (UC4).
/// </summary>
public class InvalidSubscriptionTransitionException : Exception
{
    public InvalidSubscriptionTransitionException(int subscriptionId,
        SubscriptionStatus currentStatus,
        string requestedAction,
        string legalStates)
        : base($"Cannot {requestedAction} subscription {subscriptionId} while it is {currentStatus}. " +
               $"This action requires the subscription to be {legalStates}.")
    {
        SubscriptionId = subscriptionId;
        CurrentStatus = currentStatus;
        RequestedAction = requestedAction;
    }

    public int SubscriptionId { get; }

    public SubscriptionStatus CurrentStatus { get; }

    public string RequestedAction { get; }
}
