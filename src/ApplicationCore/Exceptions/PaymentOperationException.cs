using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : Exception
{
    public int StatusCode { get; }
    public bool IsAuthorizationExpired { get; init; }

    public PaymentOperationException(string message, int statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
