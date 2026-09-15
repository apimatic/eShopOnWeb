using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidContactNumberException : Exception
{
    public InvalidContactNumberException(string message) : base(message) { }
}

public class ContactNumberNotFoundException : Exception
{
    public ContactNumberNotFoundException() : base("The contact number was not found.") { }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException() : base("The order was not found.") { }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException() : base("The notification was not found.") { }
}

public class NotificationNotResendableException : Exception
{
    public NotificationNotResendableException(string message) : base(message) { }
}
