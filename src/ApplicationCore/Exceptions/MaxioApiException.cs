using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when Maxio returns a non-success response. Carries the upstream status code and
/// error messages through so callers can decide how to surface (or recover from) them.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base($"Maxio API request failed with status {statusCode}: {string.Join(" | ", errors)}")
    {
        StatusCode = statusCode;
        Errors = errors;
    }
}
