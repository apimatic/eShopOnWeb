using System;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class PayPalPaymentException : Exception
{
    public int StatusCode { get; }

    public PayPalPaymentException(string message, int statusCode, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
