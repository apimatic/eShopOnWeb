using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Advanced Billing API.
/// </summary>
/// <remarks>
/// Advanced Billing reports validation failures as HTTP 422 with a body of either
/// <c>{"errors":["..."]}</c> or <c>{"errors":{"field":["..."]}}</c>; both shapes are flattened into
/// <see cref="Errors"/>.
/// </remarks>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors,
        string? requestId,
        string? rawBody)
        : base(BuildMessage(statusCode, method, path, errors, requestId))
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
        RequestId = requestId;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    public string Path { get; }

    /// <summary>Error messages reported by the provider, flattened from either documented shape.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Value of the provider's <c>X-Request-Id</c> response header, useful for support tickets.</summary>
    public string? RequestId { get; }

    public string? RawBody { get; }

    public bool IsValidationFailure => StatusCode == HttpStatusCode.UnprocessableEntity;

    /// <summary>
    /// True when the provider rejected the request because an application-supplied
    /// <c>reference</c> is already taken — the signal that a concurrent request won the race.
    /// </summary>
    public bool IsDuplicateReference =>
        IsValidationFailure &&
        Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors,
        string? requestId)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        var suffix = string.IsNullOrEmpty(requestId) ? string.Empty : $" (request id {requestId})";
        return $"Maxio API {method} {path} failed with {(int)statusCode} {statusCode}: {detail}{suffix}";
    }
}
