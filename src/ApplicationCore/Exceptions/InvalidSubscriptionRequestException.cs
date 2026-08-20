using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvalidSubscriptionRequestException : Exception
{
    public InvalidSubscriptionRequestException(string message) : base(message)
    {
    }
}
