using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the subscription billing integration.
/// </summary>
public abstract class SubscriptionBillingException : Exception
{
    protected SubscriptionBillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The integration is not usable because it is misconfigured (missing credentials, unknown product family, ...).
/// This is an operator problem, not a caller problem.
/// </summary>
public class SubscriptionBillingConfigurationException : SubscriptionBillingException
{
    public SubscriptionBillingConfigurationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The requested plan does not exist in the configured billing catalog.
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
/// The billing system rejected the request as invalid — for example a plan that requires a payment
/// method when none is on file. Retrying the same request unchanged will fail the same way.
/// </summary>
public class SubscriptionBillingRejectedException : SubscriptionBillingException
{
    public SubscriptionBillingRejectedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The billing system could not be reached, timed out, rate-limited us, or failed internally.
/// The request may succeed if retried later.
/// </summary>
public class SubscriptionBillingUnavailableException : SubscriptionBillingException
{
    public SubscriptionBillingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
