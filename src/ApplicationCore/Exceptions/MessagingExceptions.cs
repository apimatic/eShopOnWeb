using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusableDestinationException : Exception
{
    public UnusableDestinationException(string message) : base(message)
    {
    }
}

public class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException(string message) : base(message)
    {
    }
}

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException(string message) : base(message)
    {
    }
}

public class NotificationNotResendableException : Exception
{
    public NotificationNotResendableException(string message) : base(message)
    {
    }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(string message) : base(message)
    {
    }
}
