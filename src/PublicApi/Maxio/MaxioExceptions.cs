using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when the Maxio integration is not configured correctly
/// (for example, a required environment variable is missing).
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a call to the Maxio Advanced Billing API fails.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int upstreamStatusCode, string requestSummary, IReadOnlyList<string> errors)
        : base(BuildMessage(upstreamStatusCode, requestSummary, errors))
    {
        UpstreamStatusCode = upstreamStatusCode;
        Errors = errors;
    }

    public int UpstreamStatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, string requestSummary, IReadOnlyList<string> errors)
    {
        var detail = errors.Any() ? string.Join(" ", errors) : $"HTTP {statusCode}";
        return $"Maxio Advanced Billing request failed ({requestSummary}): {detail}";
    }
}

/// <summary>
/// Thrown when a subscription plan handle is not offered by the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' is not available on this Maxio site.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
