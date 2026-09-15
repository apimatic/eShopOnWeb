using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

public class BillingConfigurationException : BillingException
{
    public BillingConfigurationException(string message) : base(message, 500)
    {
    }
}

public class UnknownSubscriptionPlanException : BillingException
{
    public UnknownSubscriptionPlanException(string productHandle)
        : base($"Unknown subscription plan '{productHandle}'.", 400)
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
