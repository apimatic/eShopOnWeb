using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal rejects an operation or is unreachable. Carries a caller-safe message and,
/// where known, the provider's HTTP status so a genuine client-side rejection (4xx) is not reported
/// as an outage (5xx). No SDK/framework exception detail is ever surfaced through this type.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, if any. Null for transport/unknown failures.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>PayPal's debug id for the failure, useful when contacting support.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser. This integration does not build an approval round-trip; the operation stops here.
/// </summary>
public class PaymentApprovalRequiredException : PaymentGatewayException
{
    public PaymentApprovalRequiredException(string message, string? debugId = null)
        : base(message, providerStatusCode: 402, debugId: debugId)
    {
    }
}

/// <summary>
/// Raised when a stale authorization can no longer be renewed, so the order cannot be fulfilled
/// against it. The message is phrased for an operator to act on.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, int? providerStatusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, providerStatusCode, debugId, inner)
    {
    }
}

/// <summary>
/// Raised when an operation is not valid for the order/payment's current state — e.g. fulfilling an
/// order that was never authorized, or refunding more than was captured.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the caller references an order or saved card that does not exist for them. Returning
/// this rather than "forbidden" avoids revealing that another shopper's resource exists.
/// </summary>
public class PaymentEntityNotFoundException : Exception
{
    public PaymentEntityNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>Raised when the caller's request is malformed — e.g. no payment instrument, empty order.</summary>
public class InvalidPaymentRequestException : Exception
{
    public InvalidPaymentRequestException(string message) : base(message)
    {
    }
}
