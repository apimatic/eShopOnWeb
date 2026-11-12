using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan is not one the configured product family offers (-> 400).</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string message) : base(message) { }
}

/// <summary>Maxio rejected the caller's request (-> the 4xx status, 422 by default).</summary>
public class BillingRejectedException : Exception
{
    public int StatusCode { get; }
    public BillingRejectedException(string message, int statusCode = 422, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>The billing provider is unreachable / failed / outcome unknown (-> 502/503).</summary>
public class BillingUnavailableException : Exception
{
    public int StatusCode { get; }
    public BillingUnavailableException(string message, int statusCode = 502, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
