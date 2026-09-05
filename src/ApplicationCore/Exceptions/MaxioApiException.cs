using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails. Carries the upstream HTTP status
/// code and any error messages Maxio returned, so callers can decide how to surface it.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors) =>
        errors.Count > 0
            ? $"Maxio API request failed with status code {statusCode}: {string.Join("; ", errors)}"
            : $"Maxio API request failed with status code {statusCode}.";
}
