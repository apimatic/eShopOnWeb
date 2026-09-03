using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A failure returned by (or reaching) the PayPal gateway. Carries a caller-safe message plus the
/// provider's own discriminators (HTTP status, error name/issue, and PayPal's <c>debug_id</c> correlation id)
/// so the API boundary can map it coherently and operators can trace it.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(
        string message,
        int? statusCode = null,
        string? errorName = null,
        string? issue = null,
        string? debugId = null,
        Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
    }

    /// <summary>The upstream HTTP status, where one was available.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's error name (e.g. UNPROCESSABLE_ENTITY).</summary>
    public string? ErrorName { get; }

    /// <summary>PayPal's fine-grained issue code (e.g. INSTRUMENT_DECLINED).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's correlation id for support/tracing.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. 3-D Secure). This integration does not build an approval round-trip — the operation stops here.
/// </summary>
public sealed class PayPalChallengeRequiredException : PayPalGatewayException
{
    public PayPalChallengeRequiredException(string message)
        : base(message) { }
}

/// <summary>
/// An authorization has gone stale and could no longer be renewed (e.g. past PayPal's re-authorization
/// window). The operator must re-collect payment; the fulfilment cannot proceed on this authorization.
/// </summary>
public sealed class PayPalAuthorizationExpiredException : PayPalGatewayException
{
    public PayPalAuthorizationExpiredException(string message, string? issue = null, string? debugId = null, Exception? inner = null)
        : base(message, statusCode: 409, issue: issue, debugId: debugId, inner: inner) { }
}
