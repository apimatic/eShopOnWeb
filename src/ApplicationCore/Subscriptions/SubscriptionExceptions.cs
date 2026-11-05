using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
    }
}

/// <summary>The billing system refused the request (validation, business rule).</summary>
public class SubscriptionRejectedException : Exception
{
    public SubscriptionRejectedException(string message) : base(message)
    {
    }
}

/// <summary>The billing system is misconfigured, unreachable or failing.</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
