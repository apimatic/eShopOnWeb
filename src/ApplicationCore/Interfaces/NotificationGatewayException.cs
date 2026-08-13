using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Raised when the notification provider cannot be reached or reports an error for a message
/// operation. Callers that must not fail their underlying operation (placing, dispatching or
/// cancelling an order) catch this and record the reason on the notification instead.
/// </summary>
public class NotificationGatewayException : Exception
{
    public NotificationGatewayException(string message) : base(message) { }

    public NotificationGatewayException(string message, Exception inner) : base(message, inner) { }
}
