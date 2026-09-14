using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when the Maxio API returns an unsuccessful response. Carries the upstream
/// HTTP status and any error messages from the spec's error models
/// (Error-List-Response / Customer-Error-Response) for surfacing to callers.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors, string? rawBody)
        : base(BuildMessage(statusCode, operation, errors))
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

    private static string BuildMessage(HttpStatusCode statusCode, string operation, IReadOnlyList<string> errors)
    {
        var detail = errors is { Count: > 0 }
            ? string.Join("; ", errors)
            : "no error detail provided";
        return $"Maxio request '{operation}' failed with status {(int)statusCode} ({statusCode}): {detail}.";
    }
}
