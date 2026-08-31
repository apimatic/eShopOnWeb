using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a request asks a bill to make a transition its current state does not allow — for
/// example correcting a bill that has already been put to the shopper, or withdrawing one that is
/// already withdrawn. It is surfaced to the caller (as a 409) so the refusal is explicit rather
/// than a change that silently does nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message)
    {
    }
}
