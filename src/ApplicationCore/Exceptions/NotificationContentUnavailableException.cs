using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operator tries to resend a message whose content has already been disposed of and so can no
/// longer be reproduced.
/// </summary>
public class NotificationContentUnavailableException : Exception
{
    public NotificationContentUnavailableException(int notificationId)
        : base($"Notification {notificationId} has had its content disposed of and cannot be resent.")
    {
    }
}
