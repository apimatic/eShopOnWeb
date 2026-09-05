using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails. <see cref="UpstreamStatusCode"/>
/// preserves Maxio's response status so callers can distinguish a bad request (e.g. an unknown
/// plan handle) from an upstream outage.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode UpstreamStatusCode { get; }

    public MaxioApiException(HttpStatusCode upstreamStatusCode, string message) : base(message)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }
}
