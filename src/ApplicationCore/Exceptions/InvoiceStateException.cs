using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an action is refused because of the state the bill is in — e.g. correcting a bill
/// that has already been put to the shopper or withdrawn. This is an expected outcome of the
/// invoice lifecycle, not an integration defect, and the caller is told so rather than the change
/// silently doing nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message)
    {
    }
}
