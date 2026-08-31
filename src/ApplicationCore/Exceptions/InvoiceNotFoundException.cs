using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill the caller asked for cannot be found — either it does not exist, or it belongs to
/// another shopper (a shopper is never told which, so one cannot probe for another's bills).
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(int invoiceId)
        : base($"No invoice with id {invoiceId} was found.")
    {
    }
}
