using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Distinguishes the kinds of payment-gateway failure so the API boundary can map each to a
/// meaningful HTTP status and message. Whose fault a failure is drives the mapping: an
/// authentication/quota failure is ours (surfaces as 5xx), a rejected request is the caller's.
/// </summary>
public enum PaymentGatewayErrorKind
{
    /// <summary>The provider (or our credentials/quota) is at fault; not the caller's to fix.</summary>
    ProviderError,

    /// <summary>The provider could not be reached, or the outcome of a write is unknown.</summary>
    ProviderUnavailable,

    /// <summary>The caller's request was rejected as invalid.</summary>
    InvalidRequest,

    /// <summary>A conflicting state (e.g. already captured/voided).</summary>
    Conflict,

    /// <summary>The referenced PayPal resource was not found.</summary>
    NotFound,

    /// <summary>PayPal requires a shopper browser approval (3DS/PAYER_ACTION_REQUIRED). We stop rather than build an approval round-trip.</summary>
    ChallengeRequired,

    /// <summary>A stale authorization can no longer be renewed (past the reauthorization window).</summary>
    CannotReauthorize
}

/// <summary>
/// A single failure type the whole integration converts PayPal SDK failures (and transport
/// failures) into, so callers reason about one type. Carries the provider's correlation id
/// (<see cref="DebugId"/>) and, when known, the HTTP status.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayErrorKind Kind { get; }
    public int? StatusCode { get; }
    public string? DebugId { get; }

    /// <summary>PayPal's fine-grained issue code from the error body (e.g. AUTHORIZATION_EXPIRED), when present.</summary>
    public string? Issue { get; }

    public PaymentGatewayException(string message, PaymentGatewayErrorKind kind,
        int? statusCode = null, string? debugId = null, Exception? innerException = null, string? issue = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }
}
