using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, IReadOnlyList<string> errors)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }
}
