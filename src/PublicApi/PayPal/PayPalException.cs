using System;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalException : Exception
{
    public int? HttpStatus { get; }
    public string? PayPalMessage { get; }

    public PayPalException(string message, int? httpStatus = null, string? payPalMessage = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
        PayPalMessage = payPalMessage;
    }
}
