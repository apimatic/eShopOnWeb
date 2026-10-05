using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Thrown when the Maxio Advanced Billing API returns a non-success response.
/// Carries the parsed error details from the response body, which per the OpenAPI spec
/// may be a single error string, a list of error strings, or a map of field errors.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string responseBody, string? message = null, IReadOnlyList<string>? errors = null)
        : base(message ?? BuildMessage(statusCode, errors, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    public string ResponseBody { get; }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(int statusCode, IReadOnlyList<string>? errors, string responseBody)
    {
        if (errors is { Count: > 0 })
        {
            return $"Maxio API returned {(int)statusCode} ({statusCode}): {string.Join("; ", errors)}";
        }

        return $"Maxio API returned {(int)statusCode} ({statusCode}): {Truncate(responseBody)}";
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(empty response)";
        }

        return value.Length <= 500 ? value : value.Substring(0, 500) + "...";
    }
}