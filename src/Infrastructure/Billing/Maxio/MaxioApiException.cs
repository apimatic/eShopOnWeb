using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Raised by <see cref="MaxioApiClient"/> when Maxio returns a non-success response.
/// Carries the HTTP status and any error messages parsed from the Maxio error model
/// (per the spec's Error-List-Response / Customer-Error-Response schemas).
/// </summary>
internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string rawBody)
        : base(BuildMessage(statusCode, errors, rawBody))
    {
        StatusCode = statusCode;
        Errors = errors;
        RawBody = rawBody;
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RawBody { get; }

    private static string BuildMessage(int statusCode, IReadOnlyList<string> errors, string rawBody)
    {
        if (errors.Count > 0)
        {
            return $"Maxio API returned HTTP {statusCode}: {string.Join("; ", errors)}";
        }

        var snippet = string.IsNullOrWhiteSpace(rawBody)
            ? "(empty body)"
            : rawBody.Length > 500 ? rawBody.Substring(0, 500) : rawBody;
        return $"Maxio API returned HTTP {statusCode}: {snippet}";
    }
}
