using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot be found — or when it belongs to a different shopper and the caller is
/// not an operator. The two are deliberately indistinguishable so one shopper cannot probe for another's bills.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string invoiceId)
        : base($"Invoice '{invoiceId}' was not found.") { }
}
