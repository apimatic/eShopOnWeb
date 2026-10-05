using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success status code.
/// Carries the status code, raw body and the parsed error messages so callers
/// (and the API layer) can surface meaningful problems to the user.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, string responseBody, IReadOnlyList<string> errors,
        HttpRequestMethod method, string requestUri)
        : base(BuildMessage(statusCode, errors, method, requestUri))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors;
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors,
        HttpRequestMethod method, string requestUri)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "(no details)";
        return $"Maxio API {(int)statusCode} on {method} {requestUri}: {detail}";
    }
}

/// <summary>Minimal marker of the HTTP method used, for exception messages.</summary>
public enum HttpRequestMethod
{
    Get,
    Post
}