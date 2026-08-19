using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionBillingException : Exception
{
    public int StatusCode { get; }

    public SubscriptionBillingException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public SubscriptionBillingException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

public class AdvancedBillingException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public AdvancedBillingException(string message, int statusCode, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
