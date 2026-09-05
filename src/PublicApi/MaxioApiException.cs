using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation)
        : base($"Maxio Advanced Billing could not complete {operation}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
