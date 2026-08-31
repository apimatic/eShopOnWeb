using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an invoice cannot make the requested transition given the state it is in
/// (for example correcting a bill that has already been issued or withdrawn, or a transition
/// the provider legitimately refuses). This is an expected outcome, not an integration fault,
/// and the caller must be told about it rather than the change silently doing nothing.
/// </summary>
public class InvalidInvoiceOperationException : Exception
{
    public InvalidInvoiceOperationException(string message) : base(message)
    {
    }

    public InvalidInvoiceOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
