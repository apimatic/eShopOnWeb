using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotFoundException : Exception
{
    public NotificationNotFoundException() : base("The notification was not found.")
    {
    }
}
