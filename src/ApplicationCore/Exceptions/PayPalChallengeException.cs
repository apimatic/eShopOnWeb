using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal returned a contingency (PAYER_ACTION_REQUIRED) that would require the shopper
/// to approve the payment/card in a browser. This integration is server-to-server only;
/// per the requirements we surface this rather than building a browser approval round-trip.
/// </summary>
public class PayPalChallengeException : Exception
{
    public PayPalChallengeException(string message) : base(message) { }
}
