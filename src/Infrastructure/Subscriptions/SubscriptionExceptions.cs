using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' was not found in the configured product family.")
    {
    }
}

public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public MaxioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioApiException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
