using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
