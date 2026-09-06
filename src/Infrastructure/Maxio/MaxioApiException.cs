using System;
using System.Collections.Generic;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio API call completed with a non-success status. Carries the messages Maxio returned in its
/// error envelope (<c>Error-List-Response</c> / <c>Customer-Error-Response</c> in the spec).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors,
        string? rawBody)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Request path only - never includes credentials.</summary>
    public string Path { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>True when Maxio rejected the request payload (HTTP 422).</summary>
    public bool IsValidationFailure => StatusCode == HttpStatusCode.UnprocessableEntity;

    /// <summary>True when eShopOnWeb's own credentials were rejected.</summary>
    public bool IsAuthenticationFailure =>
        StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join(" ", errors) : "no error detail returned";
        return $"Maxio API call {method} {path} failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
