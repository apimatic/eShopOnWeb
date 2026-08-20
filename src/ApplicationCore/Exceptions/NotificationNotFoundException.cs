using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.")
    {
    }
}
