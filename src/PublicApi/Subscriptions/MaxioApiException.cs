using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string operation)
        : base($"Maxio rejected the {operation} request with HTTP status {statusCode}.")
    {
        StatusCode = statusCode;
        Operation = operation;
    }

    public int StatusCode { get; }
    public string Operation { get; }
}

public sealed class SubscriptionConfigurationException : Exception
{
    public SubscriptionConfigurationException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionRequestException : Exception
{
    public SubscriptionRequestException(string message) : base(message)
    {
    }
}

public sealed class SubscriptionConflictException : Exception
{
    public SubscriptionConflictException(string message) : base(message)
    {
    }
}
