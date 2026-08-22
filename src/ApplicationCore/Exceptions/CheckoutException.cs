using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public int StatusCode { get; }

    public CheckoutException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public CheckoutException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
