using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message)
    {
    }
}

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException(int contactNumberId)
        : base($"Contact number {contactNumberId} was not found.")
    {
    }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
    }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.")
    {
    }
}

public class NotificationNotResendableException : Exception
{
    public NotificationNotResendableException(int notificationId, string status)
        : base($"Notification {notificationId} cannot be resent because its delivery outcome is '{status}'.")
    {
    }
}

public class NotificationContentRedactionException : Exception
{
    public NotificationContentRedactionException(string message) : base(message)
    {
    }
}

public class MissingIdempotencyKeyException : Exception
{
    public MissingIdempotencyKeyException()
        : base("An Idempotency-Key header or request body field is required.")
    {
    }
}
