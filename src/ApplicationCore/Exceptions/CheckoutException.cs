using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public CheckoutException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
