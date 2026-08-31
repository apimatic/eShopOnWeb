using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action is not allowed by the bill's current lifecycle state — for example correcting a
/// bill that has already been put to the shopper or withdrawn. The caller is told rather than the change
/// silently doing nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message)
    {
    }
}
