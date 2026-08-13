using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown on an illegal order lifecycle transition, e.g. dispatching a cancelled order
/// (maps to HTTP 409).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
