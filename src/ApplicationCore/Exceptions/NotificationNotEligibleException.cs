using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class NotificationNotEligibleException : Exception
{
    public NotificationNotEligibleException(string message) : base(message)
    {
    }
}
