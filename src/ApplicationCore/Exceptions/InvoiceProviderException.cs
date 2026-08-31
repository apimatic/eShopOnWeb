using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment provider rejects or fails a request. Carries a caller-safe message and,
/// when the provider supplied one, a machine-readable <see cref="Reason"/> code. A refusal that is
/// an outcome of the bill's current state (for example, updating a cancelled bill) is signalled with
/// <see cref="IsStateConflict"/> set to <c>true</c> so it can be reported as a conflict rather than a fault.
/// Never carries credentials or secrets.
/// </summary>
public class InvoiceProviderException : Exception
{
    public InvoiceProviderException(string message, string? reason = null, bool isStateConflict = false, Exception? inner = null)
        : base(message, inner)
    {
        Reason = reason;
        IsStateConflict = isStateConflict;
    }

    /// <summary>Provider-supplied reason code (e.g. <c>ACTION_NOT_ALLOWED</c>), when available.</summary>
    public string? Reason { get; }

    /// <summary>True when the refusal is an expected outcome of the bill's current state.</summary>
    public bool IsStateConflict { get; }
}
