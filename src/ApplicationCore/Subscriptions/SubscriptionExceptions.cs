using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Base for subscription failures that map to a well-defined HTTP status.</summary>
public abstract class SubscriptionException : Exception
{
    protected SubscriptionException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The requested plan handle does not exist in the configured product family.</summary>
public class PlanNotFoundException : SubscriptionException
{
    public PlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.") { }
}

/// <summary>Maxio settings are missing or the billing system rejected our credentials.</summary>
public class BillingNotConfiguredException : SubscriptionException
{
    public BillingNotConfiguredException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Maxio refused the request (validation) or is unreachable / failing.</summary>
public class BillingRequestException : SubscriptionException
{
    public bool IsRejection { get; }

    public BillingRequestException(string message, bool isRejection, Exception? inner = null) : base(message, inner)
    {
        IsRejection = isRejection;
    }
}
