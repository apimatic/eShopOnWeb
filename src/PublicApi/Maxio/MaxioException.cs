using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
