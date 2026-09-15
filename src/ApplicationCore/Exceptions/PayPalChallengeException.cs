using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to approve in a
/// browser (order status PAYER_ACTION_REQUIRED). This integration is designed to be drivable without a browser,
/// so instead of building an approval round-trip we surface the challenge as an actionable error (HTTP 422).
/// </summary>
public class PayPalChallengeException : Exception
{
    public PayPalChallengeException(string message) : base(message)
    {
    }
}
