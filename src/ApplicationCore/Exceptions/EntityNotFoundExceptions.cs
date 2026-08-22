using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId) : base($"Notification {notificationId} was not found.")
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId) : base($"Contact number {contactNumberId} was not found.")
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}
