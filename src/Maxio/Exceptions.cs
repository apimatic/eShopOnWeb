using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Maxio;

/// <summary>
/// Thrown when the Maxio settings (Maxio:* configuration) are missing or invalid.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }

    public MaxioConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response.
/// Carries the HTTP status and, when available, the errors reported by Maxio.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public bool IsClientError => (int)StatusCode is >= 400 and < 500;

    public bool IsServerError => (int)StatusCode is >= 500 and < 600;

    public override string Message => Errors.Count > 0 ? string.Join(" ", Errors) : base.Message;
}

/// <summary>
/// Thrown when the requested plan handle does not exist in the configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle, string productFamilyHandle)
        : base($"No subscription plan with handle '{planHandle}' was found in product family '{productFamilyHandle}'.")
    {
        PlanHandle = planHandle;
        ProductFamilyHandle = productFamilyHandle;
    }

    public string PlanHandle { get; }

    public string ProductFamilyHandle { get; }
}
