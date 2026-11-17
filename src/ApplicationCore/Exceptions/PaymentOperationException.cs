using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? code = null, string? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? Details { get; }
}
