using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation fails. Callers treat this as an
/// upstream (billing-system) failure and surface it as a gateway error, distinct
/// from client-input problems such as an unknown plan.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Raised when the requested plan handle does not exist in the configured product
/// family. This is a client-input problem (HTTP 404), not an upstream failure.
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
