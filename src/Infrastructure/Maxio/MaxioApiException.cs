using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio Billing API call that did not succeed, either because the API returned a non-success
/// status or because the request never completed.
/// </summary>
public class MaxioApiException : Exception
{
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    public MaxioApiException(string message, HttpStatusCode? statusCode, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Errors = errors ?? NoErrors;
    }

    /// <summary>Status returned by Maxio, or null when the request never got a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Messages from the API's <c>errors</c> payload, when it supplied any.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>True when the request failed before Maxio could answer (network, DNS, timeout).</summary>
    public bool IsTransportFailure => StatusCode is null;

    /// <summary>
    /// True for a repeat of a request Maxio has already seen, identified by uniqueness token.
    /// Maxio deliberately does not say whether the original attempt succeeded, so the caller
    /// has to go and look.
    /// </summary>
    public bool IsDuplicateSubmission =>
        StatusCode == HttpStatusCode.Conflict &&
        Errors.Any(e => e.Contains("DuplicatePrevention", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when Maxio rejected the request because the <c>reference</c> is already taken.
    /// References are unique per site, which is what makes create calls idempotent.
    /// </summary>
    public bool IsReferenceAlreadyTaken =>
        StatusCode == HttpStatusCode.UnprocessableEntity &&
        Errors.Any(e => e.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                        e.Contains("unique", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when Maxio understood the request and refused it; retrying as-is will not help.</summary>
    public bool IsRejection =>
        StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Forbidden;
}
