using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Base class for payment/PayPal failures the API surfaces to callers.</summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// A PayPal API call failed. Carries PayPal's issue code (when present) and the HTTP status so the
/// endpoints can translate it into an operator-actionable message.
/// </summary>
public class PayPalApiException : PaymentException
{
    public PayPalApiException(string message, int httpStatus, string? issue = null, string? debugId = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        Issue = issue;
        DebugId = debugId;
    }

    public int HttpStatus { get; }
    public string? Issue { get; }
    public string? DebugId { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure). Per the integration's rules we stop and report rather than building
/// an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}

/// <summary>
/// An authorization can no longer be captured or renewed (it has expired or was already
/// consumed/voided). The message is phrased so an operator can act on it.
/// </summary>
public class AuthorizationUnrenewableException : PaymentException
{
    public AuthorizationUnrenewableException(string message) : base(message) { }
}
