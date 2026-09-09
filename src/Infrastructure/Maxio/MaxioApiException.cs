using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing answers with a non-success status.
/// Carries the parsed error payload per the spec's error models
/// ({"errors": [...]} arrays or {"errors": {field: message}} maps).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody, IReadOnlyList<string> errors)
        : base($"Maxio API returned {(int)statusCode} ({statusCode}): {string.Join("; ", errors.Count > 0 ? errors : new[] { responseBody ?? string.Empty })}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
    public IReadOnlyList<string> Errors { get; }
}
