using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Thrown when the Maxio integration cannot run because it is not configured.</summary>
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
/// Thrown when Maxio Advanced Billing returns an unsuccessful response.
/// Carries the HTTP status Maxio returned so callers/middleware can react.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public int StatusCode { get; }
}

/// <summary>Thrown when a requested subscription plan is not part of the configured catalog.</summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a subscribe request targets a plan while the shopper already has an
/// active subscription to another plan in the same product family.
/// </summary>
public class AlreadySubscribedException : Exception
{
    public AlreadySubscribedException(string message) : base(message)
    {
    }
}
