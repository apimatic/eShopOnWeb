using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode)
        : base($"Maxio Advanced Billing returned HTTP {(int)statusCode}.")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
