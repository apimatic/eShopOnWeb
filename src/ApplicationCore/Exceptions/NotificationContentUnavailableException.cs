using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a resend is attempted for a message whose content has already been disposed of and so
/// can no longer be re-sent.
/// </summary>
public class NotificationContentUnavailableException : Exception
{
    public NotificationContentUnavailableException(int notificationId)
        : base($"The content of notification {notificationId} has been disposed of and cannot be resent.")
    {
    }
}
