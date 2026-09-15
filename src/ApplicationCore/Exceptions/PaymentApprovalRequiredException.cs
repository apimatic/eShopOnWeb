using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to
/// approve it in a browser (for example a 3-D Secure step-up returning PAYER_ACTION_REQUIRED).
/// This integration is browser-less by design, so the operation stops and reports rather than
/// building an approval round-trip.
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}
