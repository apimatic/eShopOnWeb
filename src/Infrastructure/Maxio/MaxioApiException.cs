using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an unsuccessful response. Carries the HTTP
/// status code and any error messages Maxio reported, so callers can log/diagnose.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "no error detail returned";
        return $"Maxio API request {method} {path} failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
