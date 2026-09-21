using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a <see cref="BillingException"/> should be surfaced to the caller. Keeps distinct failures
/// distinct without leaking provider-internal detail: an authentication/quota failure is *our*
/// problem (provider unavailable), a validation failure is the caller's to fix.
/// </summary>
public enum BillingErrorKind
{
    /// <summary>The caller's request was rejected (maps to 4xx).</summary>
    Validation,

    /// <summary>The requested resource does not exist (maps to 404).</summary>
    NotFound,

    /// <summary>Our credentials, quota, or the provider itself failed (maps to 5xx).</summary>
    ProviderUnavailable,

    /// <summary>An unexpected/unclassified failure (maps to 5xx).</summary>
    Unexpected
}

/// <summary>
/// Boundary exception for the subscription-billing integration. Carries a caller-safe message and
/// a classification so the API layer can map it to a coherent HTTP response without exposing the
/// underlying SDK exception types.
/// </summary>
public sealed class BillingException : Exception
{
    public BillingErrorKind Kind { get; }

    /// <summary>Provider HTTP status when one is known; otherwise null.</summary>
    public int? ProviderStatusCode { get; }

    /// <summary>Field-level validation messages returned by the provider, when any.</summary>
    public IReadOnlyList<string> Errors { get; }

    public BillingException(
        string message,
        BillingErrorKind kind,
        int? providerStatusCode = null,
        IReadOnlyList<string>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
        Errors = errors ?? Array.Empty<string>();
    }
}
