using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal responds to a card payment with a contingency that requires the
/// shopper to approve it in a browser (e.g. a 3-D Secure challenge). This integration is
/// designed to be driven without a browser, so this condition is surfaced rather than
/// worked around with an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message)
    {
    }
}
