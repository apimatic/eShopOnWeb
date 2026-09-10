using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure reported by the PayPal gateway. Carries a caller-safe message plus PayPal's own issue code
/// and correlation id (debug id) for operators and logs — never raw card data or internal stack detail.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? issue = null, string? debugId = null,
        int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Issue = issue;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    /// <summary>PayPal's fine-grained issue code, when available (e.g. from the error details).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's correlation id for this failure, for cross-referencing with PayPal support/logs.</summary>
    public string? DebugId { get; }

    /// <summary>HTTP status PayPal returned, when available.</summary>
    public int? StatusCode { get; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. 3-D Secure). This integration does not build a browser approval round-trip; it reports
/// the condition so the caller can act on it.
/// </summary>
public class PaymentChallengeException : Exception
{
    public PaymentChallengeException(string message) : base(message) { }
}

/// <summary>A request the caller can fix (bad input, invalid state transition, amount over limit).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The requested resource does not exist or does not belong to the caller.</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}
