using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a notification cannot be found by its identifier.</summary>
public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"No notification found with id {notificationId}.")
    {
    }
}
