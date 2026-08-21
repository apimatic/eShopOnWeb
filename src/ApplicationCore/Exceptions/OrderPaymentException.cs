using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message, int statusCode = 400, string? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string? ErrorCode { get; }
}
