using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order is asked to make a lifecycle transition that is not allowed
/// from its current state (e.g. dispatching a cancelled order).
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
