using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
