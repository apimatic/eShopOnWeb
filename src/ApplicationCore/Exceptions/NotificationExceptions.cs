using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested resource does not exist, or exists but is not owned by the caller.
/// The same exception is used for both so ownership is never leaked to a caller who does not own
/// the resource. Maps to HTTP 404.
/// </summary>
public class ResourceNotFoundException : Exception
{
    public ResourceNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Thrown when the messaging provider does not consider a submitted phone number a usable
/// destination. Maps to HTTP 400. Never carries the raw phone number in a way that would be logged.
/// </summary>
public class ContactNumberValidationException : Exception
{
    public ContactNumberValidationException(string message) : base(message) { }
}

/// <summary>
/// Thrown when an order lifecycle transition is not allowed (e.g. dispatching a cancelled order).
/// Maps to HTTP 409.
/// </summary>
public class InvalidOrderStatusTransitionException : Exception
{
    public InvalidOrderStatusTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} cannot move from {from} to {to}.") { }
}
