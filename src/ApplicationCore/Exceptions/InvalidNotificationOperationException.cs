using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidNotificationOperationException : Exception
{
    public InvalidNotificationOperationException(string message) : base(message)
    {
    }
}
