using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success answer from Maxio. Kept inside the infrastructure layer - the billing service
/// translates it into the transport-agnostic exceptions in ApplicationCore.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, method, path, errors))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    /// <summary>Request path only - never the full URI, which would carry query-string values.</summary>
    public string Path { get; }

    /// <summary>Messages Maxio returned in the <c>errors</c> array, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when Maxio refused the write because the <c>reference</c> we supplied is already in use.
    /// That is the signal that an equivalent record exists and should be read back rather than
    /// created again - it is how both customer and subscription creation stay idempotent.
    /// </summary>
    public bool IsReferenceTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string method, string path, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 } ? $": {string.Join("; ", errors)}" : ".";
        return $"Maxio returned {(int)statusCode} {statusCode} for {method} {path}{detail}";
    }
}
