using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The kind of payment failure, used to map to an HTTP status the caller can act on.</summary>
public enum PaymentErrorKind
{
    /// <summary>The request was malformed or the state does not permit the action (400).</summary>
    Validation,
    /// <summary>PayPal rejected a business rule (422) — e.g. over-refund, declined capture.</summary>
    BusinessRule,
    /// <summary>The action conflicts with current state (409) — e.g. voiding an already-captured hold.</summary>
    Conflict,
    /// <summary>The referenced resource was not found (404).</summary>
    NotFound,
    /// <summary>The caller may not act on this resource (403).</summary>
    Forbidden,
    /// <summary>PayPal requires a shopper browser approval/challenge (out of scope by directive).</summary>
    ChallengeRequired,
    /// <summary>An authorization can no longer be renewed and fulfilment cannot proceed (422).</summary>
    AuthorizationNotRenewable,
    /// <summary>The PayPal gateway was unreachable or returned an unexpected response (502).</summary>
    Gateway
}

/// <summary>
/// A payment-flow failure carrying an operator-actionable message and an HTTP status. Messages are
/// safe to surface to a caller; they never contain full card details.
/// </summary>
public class PaymentException : Exception
{
    public PaymentErrorKind Kind { get; }
    public int StatusCode { get; }

    public PaymentException(string message, PaymentErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = StatusForKind(kind);
    }

    private static int StatusForKind(PaymentErrorKind kind) => kind switch
    {
        PaymentErrorKind.Validation => 400,
        PaymentErrorKind.Forbidden => 403,
        PaymentErrorKind.NotFound => 404,
        PaymentErrorKind.Conflict => 409,
        PaymentErrorKind.BusinessRule => 422,
        PaymentErrorKind.ChallengeRequired => 422,
        PaymentErrorKind.AuthorizationNotRenewable => 422,
        PaymentErrorKind.Gateway => 502,
        _ => 400
    };
}
