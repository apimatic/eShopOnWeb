using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusableDestinationException : Exception
{
    public UnusableDestinationException(string message) : base(message) { }
}

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base($"Contact number {contactNumberId} was not found.") { }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.") { }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.") { }
}

public class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message) { }
}
