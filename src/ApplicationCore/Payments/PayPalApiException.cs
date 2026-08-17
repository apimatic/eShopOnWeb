using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when PayPal rejects a request. Carries the HTTP status, PayPal's <c>debug_id</c> (needed when
/// contacting PayPal support) and the first issue code, so callers can react (e.g. renew an expired
/// authorization, or surface a declined card) without parsing raw responses.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? IssueCode { get; }

    public PayPalApiException(string message, int statusCode, string? debugId = null, string? issueCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        IssueCode = issueCode;
    }

    /// <summary>True when PayPal reported the authorization can no longer be captured because it expired.</summary>
    public bool IsAuthorizationExpired =>
        IssueCode is not null &&
        (IssueCode.Equals("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
         IssueCode.Equals("AUTH_CAPTURE_TIME_WINDOW_EXPIRED", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the card was declined by the processor/issuer.</summary>
    public bool IsInstrumentDeclined =>
        string.Equals(IssueCode, "INSTRUMENT_DECLINED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueCode, "PAYER_CANNOT_PAY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueCode, "CARD_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueCode, "TRANSACTION_REFUSED", StringComparison.OrdinalIgnoreCase);
}
