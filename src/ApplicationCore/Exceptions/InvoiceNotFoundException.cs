using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot be found, or when the caller is not allowed to see it. The two cases are
/// deliberately indistinguishable so that one shopper cannot probe for another's bills.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string invoiceId)
        : base($"Invoice '{invoiceId}' was not found.")
    {
    }
}
