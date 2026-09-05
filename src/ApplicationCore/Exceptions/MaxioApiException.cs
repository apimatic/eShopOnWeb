using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio rejects a request or is unreachable. <see cref="StatusCode"/> carries Maxio's
/// HTTP status (when known) so callers can distinguish a bad request (e.g. unknown plan handle,
/// 422/404) from an upstream outage.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public MaxioApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
