using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when Maxio Advanced Billing returns a non-success response. Carries the HTTP status
/// code and the parsed error messages, which follow the spec's error models
/// ({"errors": [...]}, {"errors": "string"} or {"error": "string"}).
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API returned HTTP {(int)statusCode}: {string.Join("; ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
