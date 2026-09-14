using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio Advanced Billing rejects a request or is unreachable. Carries the
/// upstream status code and the "errors" array Maxio returns, so callers can decide
/// whether to surface it as a client error or a service failure.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(string message, HttpStatusCode? statusCode, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public MaxioApiException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = null;
        Errors = Array.Empty<string>();
    }

    /// <summary>True when the failure was caused by the request itself (4xx) rather than an upstream/service issue.</summary>
    public bool IsClientError => StatusCode.HasValue && (int)StatusCode.Value is >= 400 and < 500;
}
