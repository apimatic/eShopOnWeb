using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API. <see cref="Errors"/> holds the messages Maxio
/// returned, parsed from the error models described by the specification.
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
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Request path only - never the query string, which can carry customer references.</summary>
    public string Path { get; }

    public IReadOnlyCollection<string> Errors { get; }

    /// <summary>True when Maxio rejected the request itself (4xx other than throttling).</summary>
    public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500 && StatusCode != HttpStatusCode.TooManyRequests;

    /// <summary>True when the request was rejected because a reference value is already taken.</summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join("; ", errors);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Maxio API call {method} {path} failed with status {(int)statusCode} ({statusCode})."
            : $"Maxio API call {method} {path} failed with status {(int)statusCode} ({statusCode}): {detail}";
    }
}
