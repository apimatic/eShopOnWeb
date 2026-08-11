using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call fails. Surfaces PayPal's own error name, issue and debug id so
/// an operator can act on it (for example, an authorization that can no longer be renewed).
/// Treated as a 502 Bad Gateway by default, since it reflects an upstream failure.
/// </summary>
public class PayPalApiException : ApiException
{
    public PayPalApiException(string message, int payPalHttpStatus, string? name, string? issue, string? debugId)
        : base(message, 502)
    {
        PayPalHttpStatus = payPalHttpStatus;
        Name = name;
        Issue = issue;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned.</summary>
    public int PayPalHttpStatus { get; }

    /// <summary>PayPal's top-level error name, e.g. UNPROCESSABLE_ENTITY.</summary>
    public string? Name { get; }

    /// <summary>PayPal's specific issue code, e.g. AUTHORIZATION_EXPIRED, PAYMENT_ALREADY_CAPTURED.</summary>
    public string? Issue { get; }

    /// <summary>PayPal's debug id, useful when contacting PayPal support.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require a shopper to
/// approve in a browser. This integration intentionally does not build an approval round-trip;
/// it stops and reports the challenge so the situation is visible rather than silently retried.
/// </summary>
public class PayPalChallengeRequiredException : ApiException
{
    public PayPalChallengeRequiredException(string message)
        : base(message, 501) { }
}
