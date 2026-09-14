using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Raised when the Maxio integration is invoked before it has been configured.</summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio Advanced Billing API returns an error response for a request.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base(string.Join(" ", errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>The HTTP status code returned by the Maxio API.</summary>
    public int StatusCode { get; }

    /// <summary>The error messages returned by the Maxio API.</summary>
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Raised when a subscription plan cannot be found in the configured product family.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' was found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
