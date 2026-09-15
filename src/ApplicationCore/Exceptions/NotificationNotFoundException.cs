using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an operation targets a notification id that does not exist.</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"No notification with id {notificationId} was found.")
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
