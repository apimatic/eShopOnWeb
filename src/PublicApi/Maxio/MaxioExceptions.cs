using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

public class MaxioProviderException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioProviderException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
