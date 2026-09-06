using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// A non-success response from the Maxio API, carrying the error messages the API reported.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string requestUri,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(statusCode, method, requestUri, errors), innerException)
    {
        StatusCode = statusCode;
        Method = method;
        RequestUri = requestUri;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Relative request path. Never contains credentials.</summary>
    public string RequestUri { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string method, string requestUri, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : statusCode.ToString();
        return $"Maxio API request {method} {requestUri} failed with status {(int)statusCode}: {detail}";
    }
}
