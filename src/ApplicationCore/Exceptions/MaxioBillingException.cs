using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a billing failure should be surfaced to the caller. The kind — not the raw provider status — is what
/// the API boundary maps to an HTTP response, so an auth/quota failure on <em>our</em> credentials is never
/// handed back to the shopper as though they caused it.
/// </summary>
public enum MaxioBillingErrorKind
{
    /// <summary>An unexpected/unclassified failure.</summary>
    Unknown = 0,

    /// <summary>The caller's request was rejected as invalid (provider 4xx validation, e.g. 422).</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist (e.g. an unknown plan handle).</summary>
    NotFound = 2,

    /// <summary>The provider is unavailable, or the failure is on our side (auth, quota, transport, provider 5xx).</summary>
    Upstream = 3,
}

/// <summary>
/// The single failure type the subscription billing abstraction raises. It carries a caller-safe message
/// only — provider exception text and internal detail are never propagated verbatim.
/// </summary>
public class MaxioBillingException : Exception
{
    public MaxioBillingException(
        string message,
        MaxioBillingErrorKind kind = MaxioBillingErrorKind.Unknown,
        int? upstreamStatusCode = null,
        IReadOnlyList<string>? providerMessages = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        UpstreamStatusCode = upstreamStatusCode;
        ProviderMessages = providerMessages ?? Array.Empty<string>();
    }

    /// <summary>The classification used to choose the caller-facing HTTP status.</summary>
    public MaxioBillingErrorKind Kind { get; }

    /// <summary>The upstream HTTP status when one was available (Case B / status-specific errors).</summary>
    public int? UpstreamStatusCode { get; }

    /// <summary>Caller-safe validation messages from the provider, when the failure was a validation rejection.</summary>
    public IReadOnlyList<string> ProviderMessages { get; }
}
