using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Represents a non-success response from the Maxio Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Raw response body returned by Maxio. Never logged or surfaced verbatim to API callers
    /// beyond the sanitized <see cref="Exception.Message"/>.
    /// </summary>
    public string ResponseBody { get; }
}
