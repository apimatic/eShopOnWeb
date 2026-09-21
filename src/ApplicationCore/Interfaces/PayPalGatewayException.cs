using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// How a PayPal failure should be treated by callers, so an HTTP boundary can map it to a coherent status
/// without knowing PayPal specifics.
/// </summary>
public enum PayPalFailureKind
{
    /// <summary>The caller's request was invalid (a 4xx the caller can act on).</summary>
    InvalidRequest = 0,

    /// <summary>The referenced resource was not found at PayPal.</summary>
    NotFound = 1,

    /// <summary>The operation conflicts with the resource's current state (e.g. already captured/voided).</summary>
    Conflict = 2,

    /// <summary>An authorization has expired and could not be captured; it may be renewable.</summary>
    AuthorizationExpired = 3,

    /// <summary>An authorization can no longer be renewed — the operator must re-authorize (re-run pay).</summary>
    AuthorizationNotRenewable = 4,

    /// <summary>PayPal is unreachable, timed out, or failed on our side (credentials/quota) — a 5xx for the caller.</summary>
    ProviderUnavailable = 5,

    /// <summary>The outcome is unknown (transport failed after the request may have been received).</summary>
    Unknown = 6
}

/// <summary>
/// The single failure type the PayPal boundary raises. Carries PayPal's own error identity (issue code and
/// debug id) plus a caller-facing classification.
/// </summary>
public sealed class PayPalGatewayException : Exception
{
    public PayPalGatewayException(
        string message,
        PayPalFailureKind kind,
        string? issueCode = null,
        string? debugId = null,
        int? statusCode = null,
        Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        IssueCode = issueCode;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    public PayPalFailureKind Kind { get; }

    /// <summary>PayPal's fine-grained issue code (e.g. AUTHORIZATION_EXPIRED), when available.</summary>
    public string? IssueCode { get; }

    /// <summary>PayPal's correlation/debug id, for support and log correlation.</summary>
    public string? DebugId { get; }

    /// <summary>The HTTP status PayPal returned, when available.</summary>
    public int? StatusCode { get; }
}
