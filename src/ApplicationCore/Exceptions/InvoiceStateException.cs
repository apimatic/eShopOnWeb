using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action is not allowed in the bill's current state — e.g. correcting a bill that has
/// already been put to the shopper or withdrawn, issuing a non-draft bill, or withdrawing one already
/// withdrawn. The caller is told rather than the action silently doing nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message)
    {
    }
}
