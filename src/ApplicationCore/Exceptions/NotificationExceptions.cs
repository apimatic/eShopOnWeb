using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusablePhoneNumberException : Exception
{
    public UnusablePhoneNumberException(string message) : base(message)
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

public class ShopperOrderNotFoundException : Exception
{
    public ShopperOrderNotFoundException(int orderId)
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

public class NotificationResendNotAllowedException : Exception
{
    public NotificationResendNotAllowedException(string message) : base(message)
    {
    }
}
