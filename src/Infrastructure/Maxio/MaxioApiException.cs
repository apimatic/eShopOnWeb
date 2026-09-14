using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio Billing API, with the parsed <c>errors</c> payload attached.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string requestDescription, IReadOnlyList<string> errors)
        : base(BuildMessage(statusCode, requestDescription, errors))
    {
        StatusCode = statusCode;
        Errors = errors;
        RequestDescription = requestDescription;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    public string RequestDescription { get; }

    /// <summary>Maxio rejects a duplicate <c>uniqueness_token</c> submission with 409 Conflict.</summary>
    public bool IsDuplicateSubmission =>
        StatusCode == HttpStatusCode.Conflict &&
        Errors.Any(e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maxio rejects a reference already taken by another record with 422 and a "must be unique"
    /// error. That is the signal that a concurrent caller won the race to create it.
    /// </summary>
    public bool IsReferenceTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("must be unique", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string requestDescription, IReadOnlyList<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio {requestDescription} failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
