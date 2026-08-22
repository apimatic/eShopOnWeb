using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationResendNotAllowedException : Exception
{
    public NotificationResendNotAllowedException(string message) : base(message)
    {
    }
}
