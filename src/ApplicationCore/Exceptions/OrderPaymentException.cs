using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public OrderPaymentException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
