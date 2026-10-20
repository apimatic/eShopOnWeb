using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>The requested plan is not offered by the configured product family.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' is not available.") { }
}

/// <summary>The billing system could not be reached or answered unusably; safe for the caller to retry.</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The billing system refused the request for a reason the caller can act on.</summary>
public class BillingRejectedException : Exception
{
    public BillingRejectedException(string message) : base(message) { }
}
