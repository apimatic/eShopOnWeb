using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"The subscription plan '{productHandle}' is not available.")
    {
    }
}

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message)
        : base(message)
    {
    }

    public MaxioConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message)
        : base(message)
    {
    }

    public SubscriptionRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class SubscriptionServiceUnavailableException : Exception
{
    public SubscriptionServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
