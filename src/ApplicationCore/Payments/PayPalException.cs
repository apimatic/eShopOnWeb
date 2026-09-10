using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when a PayPal API call fails. Carries the debug id and the first issue PayPal reported so
/// operators have something actionable, plus classification flags the payment flow reacts to.
/// </summary>
public class PayPalException : Exception
{
    public string? DebugId { get; }
    public string? IssueName { get; }
    public int StatusCode { get; }

    /// <summary>
    /// True when the failure means the authorization is no longer usable (expired/voided) and the
    /// capture should be retried against a renewed authorization instead of failing outright.
    /// </summary>
    public bool AuthorizationNeedsRenewal { get; }

    /// <summary>
    /// True when PayPal answered with a challenge that requires the shopper to approve in a browser.
    /// The integration deliberately does not build an approval round-trip; the flow surfaces this.
    /// </summary>
    public bool RequiresBuyerApproval { get; }

    public PayPalException(
        string message,
        int statusCode = 0,
        string? debugId = null,
        string? issueName = null,
        bool authorizationNeedsRenewal = false,
        bool requiresBuyerApproval = false,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        IssueName = issueName;
        AuthorizationNeedsRenewal = authorizationNeedsRenewal;
        RequiresBuyerApproval = requiresBuyerApproval;
    }
}
