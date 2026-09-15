using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. 3-D Secure): order status PAYER_ACTION_REQUIRED, or a "payer-action" HATEOAS link. The integration does
/// not build a browser approval round-trip — it stops and surfaces this so the situation can be reported.
/// </summary>
public class PayPalPayerActionRequiredException : Exception
{
    public string? PayPalOrderId { get; }
    public string? PayerActionUrl { get; }

    public PayPalPayerActionRequiredException(string? payPalOrderId, string? payerActionUrl)
        : base("PayPal requires the shopper to approve this card payment in a browser (payer action / 3-D Secure challenge). " +
               "This integration is card-direct only and does not perform a browser approval round-trip.")
    {
        PayPalOrderId = payPalOrderId;
        PayerActionUrl = payerActionUrl;
    }
}
