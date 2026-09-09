using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an unsuccessful response. Captures the HTTP status and
/// any human-readable error strings Maxio returns under {"errors": [...]}.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the failure is a unique-reference conflict on customer creation (a create/create race).</summary>
    public bool IsDuplicateReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("Reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, IReadOnlyList<string> errors, string? rawBody)
    {
        if (errors.Count > 0)
        {
            return $"Maxio API returned {(int)statusCode} {statusCode}: {string.Join("; ", errors)}";
        }

        var detail = string.IsNullOrWhiteSpace(rawBody) ? string.Empty : $": {rawBody}";
        return $"Maxio API returned {(int)statusCode} {statusCode}{detail}";
    }
}
