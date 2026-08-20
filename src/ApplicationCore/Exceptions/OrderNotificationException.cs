using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class OrderNotificationException : Exception
{
    public int StatusCode { get; }

    public OrderNotificationException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }
}
