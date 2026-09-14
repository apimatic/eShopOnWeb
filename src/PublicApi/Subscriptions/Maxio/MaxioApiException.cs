using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Maxio;

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string message,
        IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
}
