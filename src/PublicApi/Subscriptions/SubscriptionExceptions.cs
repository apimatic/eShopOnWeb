using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Thrown when Maxio settings are missing or invalid at startup.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the requested subscription plan (product handle) does not exist in the configured
/// product family or is not subscribable.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"The subscription plan '{productHandle}' was not found. Use GET /api/subscription-plans to list the plans that are available.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns an error response or cannot be reached.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    public bool IsUnprocessable => StatusCode == HttpStatusCode.UnprocessableEntity;

    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// True when the error indicates the requested resource (e.g. a customer reference) already exists.
    /// </summary>
    public bool IsDuplicate
    {
        get
        {
            if (IsConflict) return true;
            if (!IsUnprocessable) return false;

            return Message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                || Message.Contains("has already been taken", StringComparison.OrdinalIgnoreCase)
                || Message.Contains("unique", StringComparison.OrdinalIgnoreCase);
        }
    }
}
