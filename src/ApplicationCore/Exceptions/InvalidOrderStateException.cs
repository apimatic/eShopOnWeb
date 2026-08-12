using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to make a transition its current state does not allow
/// (e.g. dispatching an already-cancelled order). Surfaced to callers as a 409 Conflict.
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
