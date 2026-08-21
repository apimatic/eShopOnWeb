using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to
/// approve it in a browser (e.g. a 3-D Secure step-up). This integration is deliberately
/// no-browser, so the operation stops and reports rather than attempting an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message, string? debugId = null)
        : base(message, 409, debugId)
    {
    }
}
