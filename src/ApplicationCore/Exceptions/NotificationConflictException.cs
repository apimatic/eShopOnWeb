using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationConflictException : Exception
{
    public NotificationConflictException(string message) : base(message)
    {
    }
}
