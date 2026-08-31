using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to see or act on a bill (or order) that is not their own.
/// </summary>
public class InvoiceAccessDeniedException : Exception
{
    public InvoiceAccessDeniedException(string message) : base(message)
    {
    }
}
