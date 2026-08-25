using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PayPalGatewayException : Exception
{
    public int HttpStatusCode { get; }
    public string? PayPalErrorName { get; }

    public PayPalGatewayException(string message, int httpStatusCode = 502, string? payPalErrorName = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
        PayPalErrorName = payPalErrorName;
    }
}
