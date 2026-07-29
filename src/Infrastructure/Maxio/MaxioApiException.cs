using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a call to the Maxio Advanced Billing API fails (non-success status that the caller did
/// not expect). Carries the HTTP status code and any human-readable messages Maxio returned in its
/// { "errors": [...] } body.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, operation, errors, rawBody))
    {
        StatusCode = statusCode;
        Operation = operation;
        Errors = errors;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Operation { get; }

    public IReadOnlyList<string> Errors { get; }

    public string? RawBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors, string? rawBody)
    {
        string detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : (rawBody ?? string.Empty);

        return $"Maxio API call '{operation}' failed with status {(int)statusCode} ({statusCode})." +
               (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}");
    }
}
