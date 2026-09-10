using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to approve
/// in a browser (e.g. a 3-D Secure / payer-action redirect). This integration is browser-free by design,
/// so it stops and reports rather than building an approval round-trip.
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}
