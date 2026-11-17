using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentException : Exception
{
    public PaymentException(int statusCode, string message, string? code = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string? Code { get; }
}
