using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>A PayPal API call failed. Carries PayPal's debug id (for support) and, where
/// available, the issue name PayPal returned, so callers can react to specific conditions.</summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? debugId = null, string? issue = null)
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
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure PAYER_ACTION_REQUIRED). Per the integration mandate we stop rather
/// than building an approval round-trip.
/// </summary>
public class PayPalChallengeRequiredException : Exception
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// The card payment instrument was declined by PayPal / the issuer. The shopper should try a
/// different card rather than retrying the same one.
/// </summary>
public class PayPalInstrumentDeclinedException : Exception
{
    public PayPalInstrumentDeclinedException(string message, string? debugId = null) : base(message)
    {
        DebugId = debugId;
    }

    public string? DebugId { get; }
}

/// <summary>
/// A stale authorization could not be renewed before fulfilment. The message is phrased for an
/// operator to act on (e.g. ask the shopper to pay again).
/// </summary>
public class AuthorizationCannotBeRenewedException : Exception
{
    public AuthorizationCannotBeRenewedException(string message) : base(message) { }
}
