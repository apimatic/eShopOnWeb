using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"No notification found with id {notificationId}")
    {
    }
}

/// <summary>Raised when an operator asks to resend a message whose content has been disposed of.</summary>
public class NotificationContentUnavailableException : Exception
{
    public NotificationContentUnavailableException(int notificationId)
        : base($"The content of notification {notificationId} has been disposed of and cannot be resent.")
    {
    }
}
