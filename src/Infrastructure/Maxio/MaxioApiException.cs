using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns a non-success status. Carries the HTTP status and the raw
/// response body to aid diagnosis and mapping.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, string? responseBody)
        : base($"Maxio API {method} {path} failed with status {(int)statusCode} ({statusCode}). Body: {Truncate(responseBody)}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    private static string Truncate(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return "<empty>";
        }

        return body.Length <= 2000 ? body : body[..2000] + "…";
    }
}

/// <summary>
/// Raised when Maxio rejects a POST/PUT because a request with the same <c>uniqueness_token</c>
/// was already received within its de-duplication window (HTTP 409). The first request was
/// received but its outcome is unknown from this response alone.
/// </summary>
public sealed class MaxioDuplicateSubmissionException : MaxioApiException
{
    public MaxioDuplicateSubmissionException(string method, string path, string? responseBody)
        : base(HttpStatusCode.Conflict, method, path, responseBody)
    {
    }
}
