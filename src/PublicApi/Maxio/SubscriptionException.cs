using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A failure of the subscription flow, already translated to a caller-safe status and message.</summary>
public class SubscriptionException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public SubscriptionException(HttpStatusCode statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
