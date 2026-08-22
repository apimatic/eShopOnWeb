using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
