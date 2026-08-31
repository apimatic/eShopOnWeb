using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill is asked to make a transition its current state does not allow
/// (for example, correcting a bill that has already been put to the shopper).
/// </summary>
public class InvalidInvoiceOperationException : Exception
{
    public InvalidInvoiceOperationException(string message) : base(message)
    {
    }
}
