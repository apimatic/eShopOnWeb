using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation requires a notification's message content but that content has already
/// been disposed of at the shopper's request.
/// </summary>
public class NotificationContentDisposedException : Exception
{
    public NotificationContentDisposedException(int notificationId)
        : base($"Notification {notificationId} has had its content disposed and cannot be resent.")
    {
    }
}
