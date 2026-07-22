using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested transition is not legal from the subscription's current state, so no provider call
/// is made. The current state travels with the exception so the caller can tell the customer what to
/// do instead.
/// </summary>
public class InvalidSubscriptionOperationException : Exception
{
    public InvalidSubscriptionOperationException(string message, int subscriptionId, BillingSubscriptionState currentState)
        : base(message)
    {
        SubscriptionId = subscriptionId;
        CurrentState = currentState;
    }

    public int SubscriptionId { get; }

    public BillingSubscriptionState CurrentState { get; }
}
