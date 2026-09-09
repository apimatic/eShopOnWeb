using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, string message, IReadOnlyList<string> errors)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}

/// <summary>
/// Raised when the Maxio integration is misconfigured or the configured
/// catalog (product family / plans) cannot be found in the Maxio site.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when a request names a plan that does not exist in the configured catalog.
/// </summary>
public class MaxioPlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public IReadOnlyList<string> AvailablePlanHandles { get; }

    public MaxioPlanNotFoundException(string planHandle, IReadOnlyList<string> availablePlanHandles)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
        PlanHandle = planHandle;
        AvailablePlanHandles = availablePlanHandles;
    }
}
