using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException() : base("The contact number was not found.")
    {
    }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException() : base("The order was not found.")
    {
    }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException() : base("The notification was not found.")
    {
    }
}
