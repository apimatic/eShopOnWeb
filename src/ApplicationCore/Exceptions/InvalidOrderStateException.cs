using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation is attempted against an order whose current status does not allow it
/// (e.g. dispatching a cancelled order). Surfaces to callers as HTTP 409 Conflict.
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
