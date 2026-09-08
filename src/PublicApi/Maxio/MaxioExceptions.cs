using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when Maxio is not (or is incorrectly) configured.
/// </summary>
public sealed class MaxioConfigurationException : InvalidOperationException
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio Advanced Billing API responds with an error.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The raw response body returned by Maxio, when available.</summary>
    public string? ResponseBody { get; }

    public bool IsClientError => (int)StatusCode is >= 400 and < 500;
}

/// <summary>
/// Raised when a requested plan handle does not exist in the configured Maxio product family.
/// </summary>
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string ProductFamilyHandle { get; }
}
