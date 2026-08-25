using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public PaymentException(string message, Exception inner, int statusCode = 502)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
