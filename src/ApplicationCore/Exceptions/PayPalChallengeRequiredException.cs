using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (e.g. a 3-D Secure step or a PAYER_ACTION_REQUIRED order). This integration does
/// not build a browser approval round-trip; it stops and reports the situation instead.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message, string? approvalUrl = null)
        : base(message)
    {
        ApprovalUrl = approvalUrl;
    }

    public string? ApprovalUrl { get; }
}
