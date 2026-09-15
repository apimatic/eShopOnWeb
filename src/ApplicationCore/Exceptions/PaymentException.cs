using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Why a payment operation could not be performed. Maps to an HTTP status at the edge.</summary>
public enum PaymentErrorReason
{
    /// <summary>The order or resource was not found (or is not the caller's).</summary>
    NotFound = 404,

    /// <summary>The caller may not act on this resource.</summary>
    Forbidden = 403,

    /// <summary>The request was invalid.</summary>
    Validation = 400,

    /// <summary>The order/payment is in a state that does not allow this action.</summary>
    Conflict = 409,

    /// <summary>PayPal required a shopper browser challenge (e.g. 3-D Secure) — reported, not handled.</summary>
    ChallengeRequired = 422,

    /// <summary>A stale authorization could no longer be renewed and must be re-collected from the shopper.</summary>
    AuthorizationUnrenewable = 409,

    /// <summary>PayPal returned an error the caller cannot resolve.</summary>
    GatewayError = 502
}

/// <summary>
/// A payment operation failed for a reason an operator or shopper can act on. The <see cref="Reason"/>
/// selects the HTTP status; the message is written verbatim to the response body.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(PaymentErrorReason reason, string message, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
    }

    public PaymentErrorReason Reason { get; }
}
