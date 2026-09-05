using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, string responseBody)
        : base($"Maxio {operation} failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
