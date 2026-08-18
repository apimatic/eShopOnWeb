using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order transition is attempted that its current state does not allow,
/// for example dispatching an order that has already been cancelled.
/// </summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message)
    {
    }
}
