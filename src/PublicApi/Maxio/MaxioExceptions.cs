using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing settings are absent/incomplete so an operation cannot run.
/// Mapped to HTTP 503 Service Unavailable.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response (or cannot be reached).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(string message, int statusCode = 0, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    /// <summary>HTTP status returned by the Maxio API, or 0 when the API could not be reached.</summary>
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Raised when a shopper asks to subscribe to a plan that is not part of the configured catalog.
/// Mapped to HTTP 400 Bad Request.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The plan '{planHandle}' is not available to subscribe to.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Raised when a shopper already has an active subscription to a different plan.
/// Mapped to HTTP 409 Conflict.
/// </summary>
public class AlreadySubscribedException : Exception
{
    public AlreadySubscribedException(string currentPlan)
        : base($"You already have an active subscription ({currentPlan}). Cancel it before subscribing to another plan.")
    {
        CurrentPlan = currentPlan;
    }

    public string CurrentPlan { get; }
}

/// <summary>
/// Raised when Maxio rejects a subscription request for a validation/business reason.
/// Mapped to HTTP 400 Bad Request.
/// </summary>
public class SubscriptionRejectedException : Exception
{
    public SubscriptionRejectedException(string reason) : base(reason)
    {
    }
}
