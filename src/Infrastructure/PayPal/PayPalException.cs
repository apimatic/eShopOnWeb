using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalException : Exception
{
    public int? StatusCode { get; }

    public PayPalException(string message, Exception? inner = null) : base(message, inner) { }

    public PayPalException(string message, int statusCode, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
