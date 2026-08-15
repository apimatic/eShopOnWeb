using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. 3-D Secure / PAYER_ACTION_REQUIRED). This integration deliberately does NOT build a browser
/// approval round-trip; it surfaces the challenge to the caller instead. Maps to HTTP 422.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
