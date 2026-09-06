using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API, carrying the messages Maxio reported. Error bodies
/// follow the specification error models (<c>Error-List-Response</c>,
/// <c>Customer-Error-Response</c> and the plain-string 404 bodies).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpMethod method,
        string requestPath,
        HttpStatusCode statusCode,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(method, requestPath, statusCode, errors), innerException)
    {
        Method = method;
        RequestPath = requestPath;
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpMethod Method { get; }

    /// <summary>Path only. Never contains credentials.</summary>
    public string RequestPath { get; }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpMethod method, string requestPath, HttpStatusCode statusCode, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join(" ", errors) : "No error detail was returned.";
        return $"Maxio {method} {requestPath} responded {(int)statusCode} {statusCode}. {detail}";
    }
}

/// <summary>The Maxio API could not be reached, or did not answer within the configured timeout.</summary>
public class MaxioTransportException : Exception
{
    public MaxioTransportException(HttpMethod method, string requestPath, string reason, Exception? innerException = null)
        : base($"Maxio {method} {requestPath} failed: {reason}", innerException)
    {
        Method = method;
        RequestPath = requestPath;
    }

    public HttpMethod Method { get; }

    public string RequestPath { get; }
}
