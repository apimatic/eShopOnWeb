using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Another request for the same user/plan is still being processed.</summary>
public class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException(string message) : base(message) { }
}

/// <summary>The requested plan is not offered by the configured product family.</summary>
public class PlanNotOfferedException : Exception
{
    public PlanNotOfferedException(string message) : base(message) { }
}

/// <summary>The billing provider rejected the request in a way the caller can act on (4xx).</summary>
public class BillingRejectedException : Exception
{
    public BillingRejectedException(string message) : base(message) { }
}

/// <summary>The billing provider is unavailable, misconfigured, or answered unusably (maps to 502/503).</summary>
public class BillingUnavailableException : Exception
{
    public BillingUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
