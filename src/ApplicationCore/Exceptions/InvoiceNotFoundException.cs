using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot be found, or when it belongs to a different shopper than the caller.
/// The two are deliberately indistinguishable to the caller so that one shopper cannot probe for
/// the existence of another's bills.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(int invoiceId) : base($"No invoice found with id {invoiceId}")
    {
    }
}
