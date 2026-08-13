using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a notification-related operation conflicts with the current state
/// (e.g. dispatching an already-dispatched order, or resending a delivered message).
/// Surfaced as HTTP 409.
/// </summary>
public class OrderNotificationConflictException : Exception
{
    public OrderNotificationConflictException(string message) : base(message)
    {
    }
}
