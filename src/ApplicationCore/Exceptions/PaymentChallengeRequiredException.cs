using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to
/// approve in a browser (e.g. an order left in <c>PAYER_ACTION_REQUIRED</c>, or a <c>payer-action</c>
/// HATEOAS link). This integration is server-to-server only and deliberately does not build an
/// approval round-trip — the condition is surfaced to the operator instead.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message, HttpStatusCode? statusCode = null, string? debugId = null)
        : base(message, statusCode, debugId)
    {
    }
}
