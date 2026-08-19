using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operator action on a notification is not valid in the notification's current
/// state (e.g. re-sending a message that already reached the shopper).
/// </summary>
public class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message) { }
}
