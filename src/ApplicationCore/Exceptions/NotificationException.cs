using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationException : Exception
{
    public NotificationException(string message) : base(message)
    {
    }
}
