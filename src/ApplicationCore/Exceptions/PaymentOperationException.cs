using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An expected payment-flow failure that should be returned to the API caller with a
/// meaningful status code and message rather than surfacing as an unhandled 500.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string code, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
