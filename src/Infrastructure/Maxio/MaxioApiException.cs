using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success response.
/// Carries the HTTP status code and the error messages from the spec's error model
/// ({"errors": [ ... ]}); falls back to the raw body when the payload doesn't match.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors, string? rawBody)
    {
        if (errors.Count > 0)
        {
            return $"Maxio API returned HTTP {statusCode}: {string.Join("; ", errors)}";
        }

        return $"Maxio API returned HTTP {statusCode}. Body: {Truncate(rawBody)}";
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        return value.Length <= 512 ? value : value[..512] + "…";
    }
}
