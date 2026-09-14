using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Why a billing operation failed. The integration boundary converts every provider, transport and
/// deserialization failure into exactly one of these, so callers have a single failure taxonomy to map.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The billing integration has no usable configuration, so the capability is unavailable.</summary>
    NotConfigured,

    /// <summary>The configuration is present but does not match the billing site (for example an unknown product family).</summary>
    Misconfigured,

    /// <summary>The requested plan does not exist on the billing site.</summary>
    PlanNotFound,

    /// <summary>The billing system rejected the request as invalid — the caller can act on this.</summary>
    Validation,

    /// <summary>The request conflicts with existing billing state.</summary>
    Conflict,

    /// <summary>The billing system rejected our credentials. Never the caller's fault.</summary>
    ProviderUnauthorized,

    /// <summary>The billing system throttled us.</summary>
    RateLimited,

    /// <summary>The billing system was unreachable, timed out, or returned a server error.</summary>
    Unavailable,

    /// <summary>A write may or may not have taken effect and could not be reconciled. Never safe to blind-retry.</summary>
    UnknownOutcome,

    /// <summary>The billing system answered, but the answer could not be read.</summary>
    UnreadableResponse
}

/// <summary>
/// The single failure type the billing integration raises. Its <see cref="Message"/> is always safe to return
/// to an API caller; provider detail is logged rather than surfaced.
/// </summary>
public class BillingException : Exception
{
    public BillingException(BillingFailureKind kind, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The HTTP status the billing system returned, when one was observed.</summary>
    public int? ProviderStatusCode { get; }
}
