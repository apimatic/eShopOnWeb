using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when Maxio (Advanced Billing) is not configured correctly.
/// </summary>
public class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when the Maxio API returns an error response (or an otherwise
/// unrecoverable HTTP failure). Preserves the upstream status code and the
/// list of errors reported by Maxio so they can be surfaced to the caller.
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public int StatusCode { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// True when the upstream error is the "reference already taken" guard that Maxio
    /// enforces for customers and subscriptions. It is used to make create operations
    /// idempotent: when two identical requests race, the loser receives this error and
    /// recovers by looking up the record the winner created.
    /// </summary>
    public bool IsReferenceTakenError =>
        StatusCode == 422 &&
        Errors.Any(e => e.Contains("Reference:", StringComparison.OrdinalIgnoreCase));

    public bool IsDuplicateSubmissionError =>
        StatusCode == 409 &&
        Errors.Any(e => e.Contains("DuplicateSubmissionError", StringComparison.OrdinalIgnoreCase));
}
