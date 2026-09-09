using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thrown when the Maxio Billing API responds with a non-success status code
/// (other than the not-found cases the client translates to null results).
/// </summary>
public sealed class MaxioApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>The (possibly empty) list of error strings returned by the Billing API.</summary>
    public IReadOnlyList<string> Errors { get; }

    public string? ResponseBody { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? responseBody)
        : base($"Maxio Billing API returned {(int)statusCode} {statusCode}: " +
               (errors.Count > 0 ? string.Join("; ", errors) : "(no error details)"))
    {
        StatusCode = statusCode;
        Errors = errors;
        ResponseBody = responseBody;
    }
}
