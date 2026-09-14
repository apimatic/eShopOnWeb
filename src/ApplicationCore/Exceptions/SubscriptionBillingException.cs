using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the recurring-subscription billing integration.
/// The API layer maps each subtype onto an HTTP status code.
/// </summary>
public abstract class SubscriptionBillingException : Exception
{
    protected SubscriptionBillingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The billing integration is not configured (missing API key, subdomain, ...). Maps to 503.</summary>
public class BillingNotConfiguredException : SubscriptionBillingException
{
    public BillingNotConfiguredException(string message) : base(message)
    {
    }
}

/// <summary>The requested plan is not part of the configured catalog. Maps to 404.</summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>The billing provider rejected the request as invalid (HTTP 4xx). Maps to 422.</summary>
public class BillingRequestRejectedException : SubscriptionBillingException
{
    public BillingRequestRejectedException(IReadOnlyList<string> errors, Exception? innerException = null)
        : base(errors.Count > 0
            ? string.Join(" ", errors)
            : "The billing provider rejected the request.", innerException)
    {
        Errors = errors.ToList();
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The billing provider could not be reached, timed out, or returned an unexpected/server-side failure.
/// Maps to 502.
/// </summary>
public class BillingProviderUnavailableException : SubscriptionBillingException
{
    public BillingProviderUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
