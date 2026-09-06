using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Http;

/// <summary>
/// A non-success response from the Maxio API. Stays inside the infrastructure layer; the billing
/// service translates it into the provider-agnostic exceptions in ApplicationCore.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpMethod method,
        string path,
        HttpStatusCode statusCode,
        IEnumerable<string>? errors = null,
        string? requestId = null)
        : base(BuildMessage(method, path, statusCode, errors))
    {
        Method = method;
        Path = path;
        StatusCode = statusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
        RequestId = requestId;
    }

    public HttpMethod Method { get; }

    /// <summary>Request path, without the base address. Never carries credentials.</summary>
    public string Path { get; }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Maxio's own error messages, verbatim.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Maxio's <c>X-Request-Id</c>, for correlating with their support.</summary>
    public string? RequestId { get; }

    /// <summary>True when the failure was Maxio rejecting the request body (HTTP 422).</summary>
    public bool IsValidationFailure => StatusCode == HttpStatusCode.UnprocessableEntity;

    /// <summary>
    /// True when the failure is Maxio's uniqueness constraint on a reference we supplied — which
    /// means a concurrent request already created the record we were trying to create.
    /// </summary>
    public bool IsDuplicateReference =>
        IsValidationFailure
        && Errors.Any(e =>
            e.IndexOf("Reference", StringComparison.OrdinalIgnoreCase) >= 0
            && e.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0);

    private static string BuildMessage(HttpMethod method, string path, HttpStatusCode statusCode, IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join(" ", errors);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Maxio returned {(int)statusCode} {statusCode} for {method} {path}."
            : $"Maxio returned {(int)statusCode} {statusCode} for {method} {path}: {detail}";
    }
}
