using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails. Carries the upstream
/// HTTP status code so callers can distinguish client errors (bad plan handle, validation
/// failures) from upstream outages.
/// </summary>
public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
