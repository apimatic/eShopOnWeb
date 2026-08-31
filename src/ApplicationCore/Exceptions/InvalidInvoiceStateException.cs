using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action is not valid for the state a bill is in — correcting a bill that has already been
/// put to the shopper or withdrawn, issuing one that is withdrawn, and so on. The caller is told, rather
/// than the action silently doing nothing. Maps to HTTP 409 Conflict.
/// </summary>
public class InvalidInvoiceStateException : Exception
{
    public InvalidInvoiceStateException(int invoiceId, string message)
        : base(message)
    {
        InvoiceId = invoiceId;
    }

    public int InvoiceId { get; }
}
