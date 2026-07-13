using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidSubscriptionStateException : Exception
{
    public InvalidSubscriptionStateException(string message) : base(message)
    {
    }
}
