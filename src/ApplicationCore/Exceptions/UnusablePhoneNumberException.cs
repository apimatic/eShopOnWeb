using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnusablePhoneNumberException : Exception
{
    public UnusablePhoneNumberException(string message) : base(message)
    {
    }
}

public class OrderNotificationException : Exception
{
    public int StatusCode { get; }

    public OrderNotificationException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
