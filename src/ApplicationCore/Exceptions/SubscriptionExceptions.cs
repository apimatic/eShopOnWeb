using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan is not one the configured product family offers.</summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>The shopper already has (or is in the middle of creating) a different subscription.</summary>
public class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// The subscription request was sent to the billing provider but its outcome could not be confirmed in time.
/// It is recorded as unknown and settled on the next request.
/// </summary>
public class SubscriptionOutcomeUnknownException : Exception
{
    public SubscriptionOutcomeUnknownException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
