using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(statusCode, method, path, errors), innerException)
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Request path, without the base address, so nothing sensitive is echoed.</summary>
    public string Path { get; }

    /// <summary>Error messages as returned by the API, in order.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the API rejected a write because a caller-assigned reference is already in use.
    /// This is how a concurrent duplicate submission announces itself, and is the signal to fall
    /// back to a lookup instead of creating a second record.
    /// </summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("must be unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio API {method} {path} failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
