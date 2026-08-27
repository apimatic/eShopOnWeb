using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base("The contact number was not found.")
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base("The order was not found.")
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base("The notification was not found.")
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
