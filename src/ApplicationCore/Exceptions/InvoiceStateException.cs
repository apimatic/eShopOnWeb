using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill cannot make the requested transition because of the state it is in — for
/// example correcting a bill that has already been put to the shopper or withdrawn. The API surfaces
/// this as a 409 so the caller is told the change is no longer possible rather than it silently doing nothing.
/// </summary>
public class InvoiceStateException : Exception
{
    public InvoiceStateException(string message) : base(message) { }
}
