using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationOperationException : Exception
{
    public NotificationOperationException(string message) : base(message)
    {
    }
}
