using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class DestinationNotUsableException : Exception
{
    public DestinationNotUsableException()
        : base("The phone number is not a usable destination.")
    {
    }
}

public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}

public class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message)
    {
    }
}
