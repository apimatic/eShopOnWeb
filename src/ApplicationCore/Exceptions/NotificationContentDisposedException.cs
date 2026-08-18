using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an operation needs a notification's message content but that content has already
/// been disposed of (for example, trying to resend a message whose text has been redacted).
/// </summary>
public class NotificationContentDisposedException : Exception
{
    public NotificationContentDisposedException(int notificationId)
        : base($"Notification {notificationId} cannot be used because its content has been disposed of.")
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
