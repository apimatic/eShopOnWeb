using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a Maxio Advanced Billing API call returns an unsuccessful HTTP status. Carries the
/// status code and any error messages parsed from the response body (per the spec's error model).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, operation, errors))
    {
        StatusCode = statusCode;
        Operation = operation;
        Errors = errors;
    }

    /// <summary>The HTTP status code returned by Maxio.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>A short description of the operation that failed.</summary>
    public string Operation { get; }

    /// <summary>Error messages extracted from the response body, if any.</summary>
    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "no error detail returned";
        return $"Maxio {operation} failed with status {(int)statusCode} ({statusCode}): {detail}.";
    }
}
