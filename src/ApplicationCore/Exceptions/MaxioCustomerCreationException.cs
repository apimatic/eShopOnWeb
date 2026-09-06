using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioCustomerCreationException : MaxioApiException
{
    public MaxioCustomerCreationException(string message) : base(message)
    {
    }

    public MaxioCustomerCreationException(string message, int statusCode, string? responseBody = null)
        : base(message, statusCode, responseBody)
    {
    }

    public MaxioCustomerCreationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
