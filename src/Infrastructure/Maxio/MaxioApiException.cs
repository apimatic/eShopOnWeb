using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an unexpected/error response. Carries the upstream HTTP status
/// and any error messages Maxio returned so callers (and the API's exception middleware) can map it
/// to an appropriate response.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, IReadOnlyList<string> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Error messages returned by Maxio, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when Maxio rejected the request as a duplicate (uniqueness token collision).</summary>
    public bool IsDuplicate => StatusCode == HttpStatusCode.Conflict;

    internal static MaxioApiException FromResponse(HttpStatusCode statusCode, IReadOnlyList<string>? errors, string operation)
    {
        errors ??= Array.Empty<string>();
        var detail = errors.Count > 0 ? string.Join("; ", errors) : $"HTTP {(int)statusCode}";
        return new MaxioApiException(statusCode, errors, $"Maxio API request failed during {operation}: {detail}");
    }
}
