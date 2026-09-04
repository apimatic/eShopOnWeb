using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation)
        : base($"Maxio {operation} failed with HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
        Operation = operation;
    }

    public HttpStatusCode StatusCode { get; }
    public string Operation { get; }
}
