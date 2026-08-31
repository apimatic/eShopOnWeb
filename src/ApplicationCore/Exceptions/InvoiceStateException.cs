using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested change is not possible given the state the bill is in — for example
/// correcting a bill that has already been put to the shopper or withdrawn. The caller is told so
/// rather than the change silently doing nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message)
    {
    }
}
