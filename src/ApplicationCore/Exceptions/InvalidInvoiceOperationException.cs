using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action is attempted on a bill whose current state does not allow it — for example
/// correcting a bill that has already been put to the shopper or withdrawn. The caller is told rather
/// than the change silently doing nothing. Surfaces as HTTP 409 Conflict.
/// </summary>
public class InvalidInvoiceOperationException : Exception
{
    public InvalidInvoiceOperationException(string message) : base(message)
    {
    }
}
