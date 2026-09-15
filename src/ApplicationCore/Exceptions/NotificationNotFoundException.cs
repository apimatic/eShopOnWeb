namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotFoundException : System.Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.")
    {
    }
}
