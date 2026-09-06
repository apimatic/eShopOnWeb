using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioSubscriptionCreationException : MaxioApiException
{
    public MaxioSubscriptionCreationException(string message) : base(message)
    {
    }

    public MaxioSubscriptionCreationException(string message, int statusCode, string? responseBody = null)
        : base(message, statusCode, responseBody)
    {
    }

    public MaxioSubscriptionCreationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
