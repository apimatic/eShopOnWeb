using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan is not one of the plans this application offers.</summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>The shopper already holds (or is in the middle of creating) a subscription that conflicts with the request.</summary>
public class SubscriptionConflictException : DuplicateException
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
