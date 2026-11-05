using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' does not exist or is not available.")
    {
    }
}

/// <summary>The billing system rejected the request (e.g. validation failure).</summary>
public class BillingRejectedException : Exception
{
    public BillingRejectedException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>The billing system is not configured, unreachable or failed unexpectedly.</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
