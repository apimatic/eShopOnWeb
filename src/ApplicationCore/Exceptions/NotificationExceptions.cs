using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId) : base($"No notification found with id {notificationId}")
    {
    }
}

public class NotificationContentRedactedException : Exception
{
    public NotificationContentRedactedException(int notificationId)
        : base($"The content of notification {notificationId} has been disposed of and can no longer be re-sent")
    {
    }
}
