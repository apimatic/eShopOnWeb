using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the external billing system of record rejects a request or is unreachable/misconfigured.
/// </summary>
public class BillingProviderException : Exception
{
    public HttpStatusCode? UpstreamStatusCode { get; }
    public IReadOnlyList<string> UpstreamErrors { get; }

    public BillingProviderException(string message, HttpStatusCode? upstreamStatusCode = null, IReadOnlyList<string>? upstreamErrors = null)
        : base(message)
    {
        UpstreamStatusCode = upstreamStatusCode;
        UpstreamErrors = upstreamErrors ?? Array.Empty<string>();
    }
}
