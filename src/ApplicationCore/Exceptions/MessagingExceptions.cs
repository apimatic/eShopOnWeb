using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusablePhoneNumberException : Exception
{
    public UnusablePhoneNumberException(string message) : base(message)
    {
    }
}

public class ContactNumberAlreadyRegisteredException : Exception
{
    public ContactNumberAlreadyRegisteredException(ContactNumber existing)
        : base("This shopper already has that number on file.")
    {
        Existing = existing;
    }

    public ContactNumber Existing { get; }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(int notificationId)
        : base($"Notification {notificationId} was not found.")
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class NotificationCannotBeResentException : Exception
{
    public NotificationCannotBeResentException(string message) : base(message)
    {
    }
}
