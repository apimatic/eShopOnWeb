using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The card payment cannot complete server-side because PayPal is asking the shopper to
/// approve it in a browser (e.g. a 3-D Secure challenge). We deliberately do NOT build an
/// approval round-trip — we stop and surface this so an operator/shopper can act on it.
/// </summary>
public class PaymentChallengeException : PaymentGatewayException
{
    public PaymentChallengeException(string message, string? approvalUrl = null)
        : base(message, 409)
    {
        ApprovalUrl = approvalUrl;
    }

    /// <summary>The approval link PayPal returned, when one could be extracted.</summary>
    public string? ApprovalUrl { get; }
}
