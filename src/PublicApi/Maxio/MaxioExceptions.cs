using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio integration is invoked but the required configuration is missing.
/// </summary>
public class MaxioConfigurationException : InvalidOperationException
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a requested subscription plan is not available in the configured Maxio
/// product family (it does not exist, is archived, or requires a payment method).
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscribable plan with handle '{planHandle}' was found in the configured Maxio product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error or cannot be reached.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(BuildMessage(statusCode, message, errors))
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public MaxioApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = 0;
        Errors = Array.Empty<string>();
    }

    /// <summary>The HTTP status code returned by Maxio; 0 when the failure was not an HTTP response.</summary>
    public int StatusCode { get; }

    /// <summary>Error messages returned by Maxio, when any.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsUpstreamError => StatusCode == 0 || StatusCode >= 500 || StatusCode == 429;

    private static string BuildMessage(int statusCode, string message, IReadOnlyList<string>? errors)
    {
        if (errors != null && errors.Count > 0)
        {
            return $"{message} ({string.Join("; ", errors)})";
        }

        return message;
    }
}
