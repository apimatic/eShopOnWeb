using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(
        int statusCode,
        string code,
        string message,
        string? actionRequired = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        ActionRequired = actionRequired;
    }

    public int StatusCode { get; }

    public string Code { get; }

    public string? ActionRequired { get; }
}
