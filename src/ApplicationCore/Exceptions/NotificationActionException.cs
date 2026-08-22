using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationActionException : Exception
{
    public NotificationActionException(string message) : base(message)
    {
    }
}
