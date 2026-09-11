using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// A failure interacting with the payment provider (PayPal). The single failure type the rest of
/// the application handles, whether the underlying cause was an API error, a transport failure, or
/// an unreadable response. Carries a caller-safe message only; provider internals are logged, not surfaced.
/// </summary>
public class PaymentProcessingException : Exception
{
    public PaymentProcessingException(string message, Exception? inner = null) : base(message, inner) { }

    public PaymentProcessingException(string message, int? statusCode, string? debugId = null,
        Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    /// <summary>Transport-level HTTP status where one was available (Case B / status-bearing errors).</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal debug/correlation id from the error body, for our own logs.</summary>
    public string? DebugId { get; }

    /// <summary>
    /// True when the write may have taken effect at PayPal despite the failure (transport failure
    /// after the bytes were sent) — the outcome is unknown and must be reconciled, not assumed failed.
    /// </summary>
    public bool OutcomeUnknown { get; init; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. Per the task this integration stops and reports rather than building an approval
/// round-trip. Surfaced to the operator/shopper as an actionable error.
/// </summary>
public class PaymentApprovalRequiredException : PaymentProcessingException
{
    public PaymentApprovalRequiredException(string message) : base(message) { }
}
