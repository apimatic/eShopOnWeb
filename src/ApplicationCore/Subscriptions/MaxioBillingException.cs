using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies a Maxio billing failure so the API layer can translate it into an appropriate
/// HTTP response without leaking billing-provider internals.
/// </summary>
public enum MaxioBillingErrorKind
{
    /// <summary>Integration is misconfigured (e.g. missing API key). Maps to 500.</summary>
    Configuration,

    /// <summary>A referenced entity (e.g. a plan handle) does not exist. Maps to 404.</summary>
    NotFound,

    /// <summary>The request was rejected by Maxio validation (422). Maps to 400.</summary>
    Validation,

    /// <summary>Maxio is unavailable, timed out, or returned 5xx/429. Maps to 502/503.</summary>
    Upstream
}

/// <summary>
/// Raised when a Maxio billing operation cannot be completed. Carries a <see cref="Kind"/> for
/// response mapping plus any human-readable errors returned by Maxio.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(
        MaxioBillingErrorKind kind,
        string message,
        int? upstreamStatusCode = null,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        UpstreamStatusCode = upstreamStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }

    public MaxioBillingErrorKind Kind { get; }

    /// <summary>The HTTP status code Maxio returned, when the failure originated from a response.</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>Human-readable error messages extracted from the Maxio response body.</summary>
    public IReadOnlyList<string> Errors { get; }

    public string CombinedErrors => Errors.Any() ? string.Join("; ", Errors) : Message;
}
