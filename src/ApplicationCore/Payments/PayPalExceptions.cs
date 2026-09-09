using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A failure returned by the PayPal API. Carries the HTTP status, PayPal's <c>debug_id</c> and the
/// primary issue name so callers can react (and operators can trace the request with support).
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? debugId, string? issue)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    public int StatusCode { get; }
    public string? DebugId { get; }
    public string? Issue { get; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve in
/// a browser (e.g. a 3-D Secure step). Per the integration's scope there is no approval round-trip;
/// the operation stops and surfaces this so the caller can be told.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// Raised when an authorization can no longer be renewed (for example, it is beyond PayPal's
/// reauthorization window), so fulfilment cannot proceed. The message is phrased for an operator.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}
