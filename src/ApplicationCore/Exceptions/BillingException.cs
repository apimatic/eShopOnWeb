using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public BillingException(string message, int statusCode = 400) : base(message)
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
