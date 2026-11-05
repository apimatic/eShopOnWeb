using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Billing integration is missing required settings.</summary>
public class BillingNotConfiguredException : Exception
{
    public BillingNotConfiguredException(string message) : base(message) { }
}

/// <summary>The requested plan does not exist (or is not available) in the configured product family.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.") { }
}

/// <summary>The billing system rejected the request (e.g. validation or payment requirements).</summary>
public class BillingRejectedException : Exception
{
    public BillingRejectedException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The billing system could not be reached or failed unexpectedly.</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
