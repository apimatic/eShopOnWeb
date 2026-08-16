using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Thrown when an operation is attempted against an order in a state that does not allow it
/// (for example capturing an order that was never authorized, or refunding past the captured amount).
/// </summary>
public class InvalidOrderOperationException : Exception
{
    public InvalidOrderOperationException(string message) : base(message)
    {
    }
}
