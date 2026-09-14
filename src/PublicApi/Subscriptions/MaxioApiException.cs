using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode? statusCode, string message, bool outcomeMayBeAmbiguous = false)
        : base(message)
    {
        StatusCode = statusCode;
        OutcomeMayBeAmbiguous = outcomeMayBeAmbiguous;
    }

    public HttpStatusCode? StatusCode { get; }
    public bool OutcomeMayBeAmbiguous { get; }
}
