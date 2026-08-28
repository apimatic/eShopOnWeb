using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApiException : Exception
{
    public PaymentApiException(int statusCode, string code, string message, string? debugId = null,
        Exception? innerException = null) : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? DebugId { get; }
}

internal sealed class PayPalException : Exception
{
    public PayPalException(int statusCode, string code, string message, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        DebugId = debugId;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string? DebugId { get; }
}
