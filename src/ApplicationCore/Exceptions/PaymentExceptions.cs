using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the payment provider. Carries a caller-safe message, an optional HTTP status
/// the boundary can map, and the provider's correlation id for support/log correlation.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? statusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    /// <summary>The provider's HTTP status, when one was available.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's correlation id (PayPal <c>debug_id</c>), when one was available.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// A stale authorization could not be renewed before capture. Surfaced to the operator in terms they
/// can act on (a new payment must be collected).
/// </summary>
public class PaymentReauthorizationException : PaymentGatewayException
{
    public PaymentReauthorizationException(string message, string? debugId = null, Exception? inner = null)
        : base(message, statusCode: 409, debugId: debugId, inner: inner)
    {
    }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires a shopper to approve in a browser.
/// Per the integration's scope we STOP rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message, string? debugId = null)
        : base(message, statusCode: 402, debugId: debugId)
    {
    }
}
