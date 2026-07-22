using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested plan change cannot be attempted — for example a change to the plan
/// already in force, which is a no-op and is rejected before any provider call (UC3).
/// </summary>
public class InvalidPlanChangeException : Exception
{
    public InvalidPlanChangeException(int subscriptionId, string message)
        : base($"Cannot change the plan of subscription {subscriptionId}: {message}")
    {
        SubscriptionId = subscriptionId;
    }

    public int SubscriptionId { get; }
}
