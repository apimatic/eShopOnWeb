using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(string code, string message, int statusCode = 409) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
