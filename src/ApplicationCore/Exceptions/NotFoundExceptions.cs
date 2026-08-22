using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException() : base("Contact number was not found.")
    {
    }
}

public class OrderNotificationNotFoundException : Exception
{
    public OrderNotificationNotFoundException() : base("Notification was not found.")
    {
    }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException() : base("Order was not found.")
    {
    }
}
