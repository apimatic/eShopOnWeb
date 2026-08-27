using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps an error reported by the payment processor. Message is safe to show
/// to an operator; it never contains card details.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? payPalErrorName = null, int? httpStatusCode = null)
        : base(message)
    {
        PayPalErrorName = payPalErrorName;
        HttpStatusCode = httpStatusCode;
    }

    public string? PayPalErrorName { get; }
    public int? HttpStatusCode { get; }
}
