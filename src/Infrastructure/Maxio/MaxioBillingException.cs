using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The kinds of failure the Maxio integration can surface to callers. Each kind
/// maps deliberately to an HTTP outcome at the API boundary.
/// </summary>
public enum MaxioBillingFailureKind
{
    /// <summary>The billing integration is not usable for its intended operation.</summary>
    NotConfigured,

    /// <summary>The requested resource does not exist in Maxio.</summary>
    NotFound,

    /// <summary>Maxio rejected the request as invalid (a client error).</summary>
    Validation,

    /// <summary>Maxio itself failed (5xx or an unreadable provider response).</summary>
    Provider,

    /// <summary>Maxio could not be reached at all (transport failure, timeout).</summary>
    Unavailable
}

/// <summary>
/// The single error type the Maxio integration raises across its boundary — both
/// API errors (non-2xx responses) and transport failures are normalized here, with
/// caller-safe messages only.
/// </summary>
public sealed class MaxioBillingException : Exception
{
    public MaxioBillingException(
        MaxioBillingFailureKind kind,
        string message,
        int? providerStatusCode = null,
        IReadOnlyList<string>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Details = details ?? Array.Empty<string>();
    }

    public MaxioBillingFailureKind Kind { get; }

    /// <summary>The HTTP status Maxio returned, when the failure is an API error.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Caller-safe detail messages (e.g. Maxio validation errors), never raw bodies.</summary>
    public IReadOnlyList<string> Details { get; }
}