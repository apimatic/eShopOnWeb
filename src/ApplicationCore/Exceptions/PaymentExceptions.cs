using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment-related resource (order, payment, saved card) cannot be found for the caller.
/// Maps to HTTP 404. Also used for cross-shopper access attempts, so ownership is never revealed.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a payment operation is not valid for the current state of the order/payment
/// (e.g. capturing an order that was never authorized, cancelling one already fulfilled).
/// Maps to HTTP 409 Conflict.
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a request payload is semantically invalid (e.g. refund amount exceeds captured amount,
/// no payment instrument supplied). Maps to HTTP 400 Bad Request.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (3-D Secure / payer action). Per the task we STOP and report rather than building an
/// approval round-trip. Maps to HTTP 409 with an actionable message (carries the approval URL when known).
/// </summary>
public class PaymentApprovalRequiredException : Exception
{
    public string? ApprovalUrl { get; }

    public PaymentApprovalRequiredException(string message, string? approvalUrl = null) : base(message)
    {
        ApprovalUrl = approvalUrl;
    }
}

/// <summary>
/// Wraps a failure reported by (or while talking to) PayPal. Carries the provider HTTP status so the
/// boundary can map a provider 4xx (caller can act on it) to a client 4xx and a transport/unknown
/// failure to a 5xx. Never carries raw SDK/framework exception text intended for the wire.
/// </summary>
public class PayPalApiException : Exception
{
    /// <summary>The HTTP status PayPal returned, when known.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>True when the failure is a deterministic provider rejection the caller can act on (4xx).</summary>
    public bool IsClientError { get; }

    public PayPalApiException(string message, int? providerStatusCode, bool isClientError, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatusCode = providerStatusCode;
        IsClientError = isClientError;
    }
}
