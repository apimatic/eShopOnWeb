using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public abstract class MaxioException : Exception
{
    protected MaxioException(string message)
        : base(message)
    {
    }

    protected MaxioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class MaxioConfigurationException : MaxioException
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }
}

public class SubscriptionPlanNotFoundException : MaxioException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' is not available in the configured Maxio product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

public class MaxioApiException : MaxioException
{
    public MaxioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
