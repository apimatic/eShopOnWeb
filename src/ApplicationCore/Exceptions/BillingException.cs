using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public BillingException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
