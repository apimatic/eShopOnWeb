using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot be found for the caller. Also used when a bill exists but belongs to a
/// different shopper, so that ownership is never revealed to someone who does not own it.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string invoiceId)
        : base($"No invoice found with id {invoiceId}")
    {
    }
}
