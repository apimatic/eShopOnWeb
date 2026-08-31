using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a bill cannot be found for the caller. Deliberately also used when a bill exists but
/// belongs to another shopper, so one shopper cannot even learn of another's bill.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(int invoiceId)
        : base($"No invoice with id {invoiceId} was found.")
    {
    }

    public InvoiceNotFoundException(string message) : base(message)
    {
    }

    public InvoiceNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
