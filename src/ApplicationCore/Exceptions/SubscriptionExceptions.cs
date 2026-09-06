using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base type for every failure raised by the subscription-billing capability.</summary>
public abstract class SubscriptionException : Exception
{
    protected SubscriptionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The requested plan handle is not published by the billing system.</summary>
public class SubscriptionPlanNotFoundException : SubscriptionException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The subscription-billing integration has not been configured (missing API key or subdomain), so
/// the capability is unavailable rather than broken.
/// </summary>
public class SubscriptionBillingNotConfiguredException : SubscriptionException
{
    public SubscriptionBillingNotConfiguredException(string message) : base(message)
    {
    }
}

/// <summary>
/// A concurrent or replayed submission is still in flight upstream and its outcome is not yet
/// knowable. The caller should retry shortly.
/// </summary>
public class SubscriptionInProgressException : SubscriptionException
{
    public SubscriptionInProgressException(string message) : base(message)
    {
    }
}

/// <summary>
/// The billing system rejected the request or was unreachable. Carries the upstream status code so
/// callers can distinguish "we sent something bad" from "the provider is down".
/// </summary>
public class SubscriptionBillingException : SubscriptionException
{
    public SubscriptionBillingException(string message, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }

    public int? UpstreamStatusCode { get; }

    /// <summary>True when the billing system rejected our request as invalid (4xx other than 429).</summary>
    public bool IsClientError =>
        UpstreamStatusCode is >= 400 and < 500 && UpstreamStatusCode != 429;
}
