using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper
/// to approve in a browser (order status PAYER_ACTION_REQUIRED, or a payer-action / approve link).
/// Per the integration mandate this is a stop-and-report condition — the integration deliberately
/// does NOT build a browser approval round-trip — so it surfaces as an unprocessable request.
/// </summary>
public class PaymentChallengeRequiredException : PaymentProcessorException
{
    public PaymentChallengeRequiredException(string message, Exception? innerException = null)
        : base(message, statusCode: 422, innerException)
    {
    }
}
