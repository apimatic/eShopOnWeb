using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that would require the shopper to approve in a browser
/// (e.g. a 3-D Secure step-up). This integration is browser-free by design, so the operation stops here
/// and reports the challenge rather than building an approval round-trip.
/// </summary>
public class PaymentApprovalRequiredException : PaymentException
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}
