using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when the subscription billing provider cannot fulfil a request. Carries an optional
/// upstream HTTP status code so callers can translate it into an appropriate API response.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }

    public int? UpstreamStatusCode { get; }
}

/// <summary>
/// Raised when a requested plan handle does not exist within the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when the billing integration has not been configured (missing credentials/settings).
/// </summary>
public class BillingNotConfiguredException : SubscriptionBillingException
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}
