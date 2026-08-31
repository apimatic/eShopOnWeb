using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when a bill cannot be found, or is not visible to the caller.</summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string invoiceId)
        : base($"No invoice found with id {invoiceId}")
    {
    }
}
