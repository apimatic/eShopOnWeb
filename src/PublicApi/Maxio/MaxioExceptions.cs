using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

public class MaxioApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public MaxioApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
