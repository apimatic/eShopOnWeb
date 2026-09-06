using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Maxio API, with the error strings Maxio returned.
/// </summary>
/// <remarks>
/// This type stays inside the infrastructure layer; <see cref="MaxioSubscriptionService"/>
/// translates it into the provider-neutral exceptions declared in ApplicationCore.
/// </remarks>
public sealed class MaxioApiException : Exception
{
    public MaxioApiException(
        HttpMethod method,
        string path,
        HttpStatusCode statusCode,
        IEnumerable<string>? errors = null,
        Exception? innerException = null)
        : base(BuildMessage(method, path, statusCode, errors), innerException)
    {
        Method = method.Method;
        Path = path;
        StatusCode = statusCode;
        Errors = errors?.ToArray() ?? Array.Empty<string>();
    }

    public string Method { get; }
    public string Path { get; }
    public HttpStatusCode StatusCode { get; }
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True for the 409 Maxio returns when a uniqueness_token has already been used.</summary>
    public bool IsDuplicateSubmission =>
        StatusCode == HttpStatusCode.Conflict
        || Errors.Any(e => e.Contains("DuplicateSubmission", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a create was rejected because the reference is already taken.</summary>
    public bool IsReferenceTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity
        && Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase)
            && (e.Contains("taken", StringComparison.OrdinalIgnoreCase)
                || e.Contains("already", StringComparison.OrdinalIgnoreCase)
                || e.Contains("unique", StringComparison.OrdinalIgnoreCase)));

    private static string BuildMessage(
        HttpMethod method,
        string path,
        HttpStatusCode statusCode,
        IEnumerable<string>? errors)
    {
        var detail = errors is null ? null : string.Join("; ", errors);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}";
        return $"Maxio API {method.Method} /{path.TrimStart('/')} returned {(int)statusCode} {statusCode}{suffix}";
    }
}
