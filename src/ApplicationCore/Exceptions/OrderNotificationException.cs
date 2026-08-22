using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotificationException : Exception
{
    public OrderNotificationException(string message) : base(message)
    {
    }
}
