using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Raised when Maxio is referenced but its required settings are missing or invalid.
/// </summary>
public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// </summary>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IEnumerable<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors.ToList();
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    /// <summary>
    /// True when Maxio rejected the request because a unique field (customer or
    /// subscription <c>reference</c>) was already taken. Callers use this as the
    /// signal for an idempotent create: another, identical request already won.
    /// </summary>
    public bool IsReferenceConflict =>
        Errors.Any(e => e.Contains("Reference:", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, IEnumerable<string> errors, string? rawBody)
    {
        var list = errors.ToList();
        var detail = list.Count > 0 ? string.Join(" ", list) : rawBody ?? "(no error body)";
        return $"Maxio Advanced Billing returned HTTP {(int)statusCode} ({statusCode}): {detail}";
    }
}
