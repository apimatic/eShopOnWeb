using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to
/// approve it in a browser (e.g. a 3-D Secure step-up). This integration is designed to be driven
/// without a browser, so rather than building an approval round-trip it stops and reports the
/// condition to the caller.
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public PaymentApprovalRequiredException(string message) : base(message)
    {
    }
}
