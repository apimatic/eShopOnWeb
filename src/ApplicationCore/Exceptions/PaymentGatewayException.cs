using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the payment provider (PayPal). Carries a caller-safe message plus the provider's
/// own identity fields (error name and correlation/debug id) for logging and operator action. This is the
/// single failure type the payment endpoints handle — the gateway translates every SDK/transport failure
/// into it at its boundary.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? inner = null,
        int? statusCode = null, string? errorName = null, string? debugId = null,
        PaymentGatewayFailureKind kind = PaymentGatewayFailureKind.Provider)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        DebugId = debugId;
        Kind = kind;
    }

    /// <summary>Provider HTTP status where known (Case B errors and transport carry one; some typed errors do not).</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error name/code where present (e.g. UNPROCESSABLE_ENTITY).</summary>
    public string? ErrorName { get; }

    /// <summary>PayPal's debug_id — the correlation id to quote when investigating.</summary>
    public string? DebugId { get; }

    public PaymentGatewayFailureKind Kind { get; }
}

/// <summary>Coarse classification so endpoints can pick a sensible HTTP status.</summary>
public enum PaymentGatewayFailureKind
{
    /// <summary>The provider rejected the caller's request in a way they can act on (validation, 4xx).</summary>
    CallerError = 0,

    /// <summary>Our credentials/quota or the provider itself failed — not the caller's fault.</summary>
    Provider = 1,

    /// <summary>The provider requires a browser approval (3DS / PAYER_ACTION_REQUIRED) — unsupported here.</summary>
    ApprovalRequired = 2,

    /// <summary>A stale authorization could not be renewed and the order cannot be fulfilled as-is.</summary>
    AuthorizationUnrenewable = 3
}
