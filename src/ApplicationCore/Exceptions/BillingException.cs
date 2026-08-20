using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
