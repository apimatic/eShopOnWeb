using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal responds to a card payment with a contingency that requires the
/// shopper to approve in a browser (e.g. a 3-D Secure challenge). This integration is
/// browser-less by design, so the operation stops and reports rather than building an
/// approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
