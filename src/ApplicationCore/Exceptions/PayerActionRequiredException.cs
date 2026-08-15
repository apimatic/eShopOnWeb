using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (e.g. 3-D Secure). This integration is browser-less by design, so this surfaces as
/// an explicit, non-retryable error rather than an approval round-trip.
/// </summary>
public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException(string message) : base(message)
    {
    }
}
