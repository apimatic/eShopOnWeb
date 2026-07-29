using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio API call returns an unexpected (non-success) HTTP response. Carries the
/// status code and any error messages Maxio returned, for diagnostics and status-code mapping.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>True for 4xx responses caused by the request itself (excluding 429 rate limiting).</summary>
    public bool IsClientError =>
        (int)StatusCode >= 400 && (int)StatusCode < 500 && StatusCode != HttpStatusCode.TooManyRequests;
}
