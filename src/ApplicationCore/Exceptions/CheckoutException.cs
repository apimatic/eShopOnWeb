using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class CheckoutException : Exception
{
    public int StatusCode { get; }

    public CheckoutException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
