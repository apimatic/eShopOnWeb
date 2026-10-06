using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Base class for failures reported by - or while talking to - the external billing system (Maxio).
/// </summary>
public class BillingProviderException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public BillingProviderException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}

/// <summary>
/// The request was rejected by the billing system because the submitted data is not acceptable
/// (Maxio answers 422/400 with an <c>errors</c> payload). Surface the messages to the caller.
/// </summary>
public class BillingRequestRejectedException : BillingProviderException
{
    public BillingRequestRejectedException(string message, IReadOnlyList<string> errors)
        : base(422, message, errors)
    {
    }
}

/// <summary>
/// The billing system could not be reached, answered with a server side error, or is not
/// configured correctly. Treated as a transient/unavailable condition by the API layer.
/// </summary>
public class BillingServiceUnavailableException : BillingProviderException
{
    public BillingServiceUnavailableException(string message, int statusCode = 0, IReadOnlyList<string>? errors = null)
        : base(statusCode, message, errors)
    {
    }
}

/// <summary>
/// The plan handle supplied by the shopper does not exist in the configured Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' is not available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
