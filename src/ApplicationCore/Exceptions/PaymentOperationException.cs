using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not be completed. The status code is an HTTP
/// status that is safe and useful to return to the caller.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? code = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}
