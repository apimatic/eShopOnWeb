using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised while talking to the billing system of record.
/// </summary>
public abstract class BillingException : Exception
{
    protected BillingException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Provider-reported error messages, safe to surface to the caller.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The requested plan is not offered by the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The billing provider rejected the request as invalid (for example a plan that requires a payment
/// method that has not been captured).
/// </summary>
public class BillingValidationException : BillingException
{
    public BillingValidationException(string message, IEnumerable<string>? errors = null)
        : base(message, errors)
    {
    }
}

/// <summary>
/// A competing request for the same shopper and plan is still in flight, so the outcome of this one
/// cannot be determined yet. Retrying after a short delay is safe.
/// </summary>
public class BillingConflictException : BillingException
{
    public BillingConflictException(string message, IEnumerable<string>? errors = null)
        : base(message, errors)
    {
    }
}

/// <summary>
/// The billing provider could not be reached, or answered with a failure that is neither the caller's
/// fault nor retryable within the request.
/// </summary>
public class BillingUnavailableException : BillingException
{
    public BillingUnavailableException(string message, IEnumerable<string>? errors = null, Exception? innerException = null)
        : base(message, errors, innerException)
    {
    }
}

/// <summary>
/// Subscription billing has not been configured for this deployment.
/// </summary>
public class BillingNotConfiguredException : BillingException
{
    public BillingNotConfiguredException(string message)
        : base(message)
    {
    }
}
