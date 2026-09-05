using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation)
        : base($"Maxio request failed while {operation} (HTTP {(int)statusCode}).")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
