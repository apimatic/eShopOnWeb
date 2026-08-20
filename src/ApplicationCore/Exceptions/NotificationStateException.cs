using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationStateException : Exception
{
    public NotificationStateException(string message) : base(message)
    {
    }
}
