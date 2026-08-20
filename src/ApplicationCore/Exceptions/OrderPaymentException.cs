using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public int StatusCode { get; }

    public OrderPaymentException(string message, int statusCode = 409) : base(message)
    {
        StatusCode = statusCode;
    }
}
