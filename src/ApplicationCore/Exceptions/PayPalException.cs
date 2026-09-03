using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal integration boundary raises. Carries a caller-safe message plus,
/// where available, the transport status and PayPal's own error identity (issue code + correlation
/// <c>debug_id</c>) so the API layer can map it coherently and an operator can act on it — without ever
/// leaking raw SDK/provider exception detail.
/// </summary>
public class PayPalException : Exception
{
    public PayPalException(string message, int? statusCode = null, string? issue = null,
        string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Issue = issue;
        DebugId = debugId;
    }

    /// <summary>Transport/HTTP status where PayPal supplied one; null for typed errors that carry none.</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's fine-grained application-level issue code (e.g. <c>INSTRUMENT_DECLINED</c>).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's correlation id for the failed call.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve in a
/// browser. The integration deliberately does not build an approval round-trip; the operation stops and
/// this surfaces to the caller.
/// </summary>
public class PayPalApprovalRequiredException : PayPalException
{
    public PayPalApprovalRequiredException(string message)
        : base(message) { }
}
