using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
