using System;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }

    public PayPalException(string message, int statusCode = 500, string? errorCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }
}
