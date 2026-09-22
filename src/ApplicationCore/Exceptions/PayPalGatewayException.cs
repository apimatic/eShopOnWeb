using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a PayPal gateway failure should be surfaced to the caller. Chosen from the PayPal error
/// body / status at the gateway boundary so the HTTP layer can map it without re-inspecting SDK types.
/// </summary>
public enum PaymentGatewayErrorKind
{
    /// <summary>The caller sent something PayPal rejected (validation) — a 4xx the caller can fix.</summary>
    Validation,

    /// <summary>The referenced PayPal resource was not found.</summary>
    NotFound,

    /// <summary>The operation conflicts with current state (e.g. already captured/voided).</summary>
    Conflict,

    /// <summary>A stale authorization could not be renewed — the operator must re-collect payment.</summary>
    AuthorizationNotRenewable,

    /// <summary>The card requires shopper approval (e.g. a 3DS browser challenge) this integration does not perform.</summary>
    ApprovalRequired,

    /// <summary>PayPal is unreachable, our credentials/quota failed, or an unexpected 5xx — not the caller's fault.</summary>
    Unavailable,

    /// <summary>The transport failed after the request may have been received — outcome unknown.</summary>
    Unknown
}

/// <summary>
/// A PayPal failure translated at the gateway boundary. Carries a caller-safe message plus the
/// PayPal correlation id (<see cref="DebugId"/>) and issue name for operator logs — never the raw
/// SDK exception text or any request body.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, PaymentGatewayErrorKind kind,
        int? statusCode = null, string? issue = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }

    public PaymentGatewayErrorKind Kind { get; }
    public int? StatusCode { get; }

    /// <summary>The PayPal issue name (e.g. <c>INSTRUMENT_DECLINED</c>), when present.</summary>
    public string? Issue { get; }

    /// <summary>The PayPal <c>debug_id</c> correlation id, for tracing in PayPal's logs.</summary>
    public string? DebugId { get; }
}
