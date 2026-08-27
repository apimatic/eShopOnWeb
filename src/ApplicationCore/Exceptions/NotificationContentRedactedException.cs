using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationContentRedactedException : Exception
{
    public NotificationContentRedactedException(int notificationId)
        : base($"Notification {notificationId} can no longer be re-sent: its content has been disposed of.")
    {
    }
}
