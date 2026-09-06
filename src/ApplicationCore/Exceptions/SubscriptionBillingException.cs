using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base type for failures raised by the subscription billing integration.
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
/// The requested plan does not exist in the configured catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured catalog.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// The billing system rejected the request as invalid. Caller-fixable.
/// </summary>
public class SubscriptionBillingValidationException : SubscriptionBillingException
{
    public SubscriptionBillingValidationException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// The billing system could not be reached, or failed in a way the caller cannot fix
/// (transport failure, rate limiting, bad credentials, provider outage).
/// </summary>
public class SubscriptionBillingUnavailableException : SubscriptionBillingException
{
    public SubscriptionBillingUnavailableException(string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        RetryAfter = retryAfter;
    }

    public SubscriptionBillingUnavailableException(string message, Exception innerException, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    /// <summary>How long the caller should wait before retrying, when the provider told us.</summary>
    public TimeSpan? RetryAfter { get; }
}
