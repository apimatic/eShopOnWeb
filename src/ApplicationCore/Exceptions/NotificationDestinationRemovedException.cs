using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationDestinationRemovedException : Exception
{
    public NotificationDestinationRemovedException(int notificationId)
        : base($"Notification {notificationId} can no longer be re-sent: its destination number is no longer registered.")
    {
    }
}
