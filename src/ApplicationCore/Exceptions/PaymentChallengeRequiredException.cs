using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that would require a shopper to approve in a
/// browser. This headless integration does not build an approval round-trip — it stops and reports.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message, Exception? innerException = null)
        : base(message, clientStatusCode: 422, innerException)
    {
    }
}
