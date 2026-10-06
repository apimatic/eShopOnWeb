using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio Advanced Billing API returns a non-success HTTP status.
/// Carries the status code and any parsed error messages so callers can translate
/// them into an appropriate HTTP response.
/// </summary>
public class MaxioApiException : Exception
{
    public int StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }
    public string? RawBody { get; }

    public MaxioApiException(int statusCode, IReadOnlyList<string> errors, string? rawBody = null)
        : base(BuildMessage(statusCode, errors))
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
        RawBody = rawBody;
    }

    private static string BuildMessage(int statusCode, IReadOnlyList<string>? errors)
    {
        var detail = errors is { Count: > 0 } ? string.Join("; ", errors) : "(no error details)";
        return $"Maxio Advanced Billing request failed with HTTP {statusCode}: {detail}";
    }
}
