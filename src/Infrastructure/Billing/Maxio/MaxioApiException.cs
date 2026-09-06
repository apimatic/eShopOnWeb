using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// A non-success response from the Maxio Advanced Billing API.
/// </summary>
public class MaxioApiException : Exception
{
    /// <summary>
    /// Error string Maxio returns when a <c>uniqueness_token</c> replay is rejected.
    /// </summary>
    public const string DuplicateSubmissionError = "DuplicatePrevention::DuplicateSubmissionError";

    public MaxioApiException(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors,
        Exception? innerException = null)
        : base(BuildMessage(statusCode, method, path, errors), innerException)
    {
        StatusCode = statusCode;
        Method = method;
        Path = path;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    public string Method { get; }

    public string Path { get; }

    /// <summary>Messages from Maxio's <c>errors</c> payload, verbatim.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when this is Maxio rejecting a replayed <c>uniqueness_token</c>.</summary>
    public bool IsDuplicateSubmission =>
        StatusCode == HttpStatusCode.Conflict &&
        Errors.Any(e => e.Contains("DuplicatePrevention", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when Maxio rejected a customer because its reference is already taken.</summary>
    public bool IsDuplicateCustomerReference =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e =>
            e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
            e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(
        HttpStatusCode statusCode,
        string method,
        string path,
        IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio API {method} {path} failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
