using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Raised when an operator asks to resend a message that is not eligible for a resend.</summary>
public class NotificationNotResendableException : Exception
{
    public NotificationNotResendableException(int notificationId, string reason)
        : base($"Notification {notificationId} cannot be resent: {reason}")
    {
    }
}
