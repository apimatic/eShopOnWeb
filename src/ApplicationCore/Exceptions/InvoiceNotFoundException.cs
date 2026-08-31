using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot be found for the caller. This is deliberately also used when a bill
/// exists but belongs to another shopper, so that one shopper cannot even learn of another's bill.
/// Surfaces as HTTP 404 Not Found.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string message) : base(message)
    {
    }
}
