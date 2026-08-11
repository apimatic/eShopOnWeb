using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (3-D Secure / payer-action). This integration is explicitly browser-free, so rather than building
/// an approval round-trip it stops and surfaces the situation. Maps to HTTP 422.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
