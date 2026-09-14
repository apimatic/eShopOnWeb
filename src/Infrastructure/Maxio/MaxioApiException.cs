using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A non-success response from the Maxio API, with the upstream error list parsed out.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string requestDescription, IReadOnlyCollection<string> errors, Exception? innerException = null)
        : base(BuildMessage(statusCode, requestDescription, errors), innerException)
    {
        StatusCode = statusCode;
        RequestDescription = requestDescription;
        Errors = errors;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Method and path of the failed call, e.g. "POST subscriptions.json". Never contains credentials.</summary>
    public string RequestDescription { get; }

    public IReadOnlyCollection<string> Errors { get; }

    /// <summary>
    /// True when Maxio rejected the call because an identical request carrying the same
    /// uniqueness_token was already received.
    /// </summary>
    public bool IsDuplicateSubmission =>
        StatusCode == HttpStatusCode.Conflict &&
        Errors.Any(e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));

    private static string BuildMessage(HttpStatusCode statusCode, string requestDescription, IReadOnlyCollection<string> errors)
    {
        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error detail returned";
        return $"Maxio API call '{requestDescription}' failed with {(int)statusCode} {statusCode}: {detail}";
    }
}
