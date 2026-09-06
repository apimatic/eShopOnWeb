using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API, carrying the status code and any
/// messages parsed out of the spec's error models.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        IEnumerable<string>? errors = null,
        Exception? innerException = null)
        : base(BuildMessage(statusCode, method, path, errors), innerException)
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray() ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Request path only. Never contains the API key, which travels in the Authorization header.</summary>
    public string Path { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));
        var summary = $"Maxio API returned {(int)statusCode} {statusCode} for {method} {path}";
        return string.IsNullOrWhiteSpace(detail) ? summary + "." : $"{summary}: {detail}";
    }
}
