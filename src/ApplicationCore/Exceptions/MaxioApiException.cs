using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(string message) : base(message)
    {
    }

    public MaxioApiException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public int? StatusCode { get; init; }
}
