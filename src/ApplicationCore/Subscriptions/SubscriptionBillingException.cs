using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when the billing system of record cannot fulfil a subscription operation
/// (misconfiguration, an upstream error, or a violated precondition).
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message) { }

    public SubscriptionBillingException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Raised when the requested plan handle does not exist in the configured product family.
/// </summary>
public class PlanNotFoundException : SubscriptionBillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
