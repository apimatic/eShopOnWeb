using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}
