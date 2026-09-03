using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// How a billing failure should be surfaced to the caller. The billing service classifies every
/// provider/transport failure into one of these so the API layer can map it to an HTTP status
/// without knowing anything about the billing SDK.
/// </summary>
public enum BillingErrorKind
{
    /// <summary>The caller's request was rejected as invalid (maps to 400).</summary>
    Validation,

    /// <summary>A referenced resource does not exist (maps to 404).</summary>
    NotFound,

    /// <summary>
    /// The billing provider is unavailable, misconfigured, throttling us, or unreachable — the caller
    /// did nothing wrong and cannot fix it (maps to 502/503).
    /// </summary>
    ProviderUnavailable,

    /// <summary>An unmapped/unexpected failure (maps to 500/502).</summary>
    Unknown,
}

/// <summary>
/// The single failure type the billing abstraction raises. Carries a caller-safe message and a
/// <see cref="BillingErrorKind"/>; the underlying provider/transport exception is preserved as the
/// inner exception for server-side diagnostics but is never surfaced to callers.
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(BillingErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public BillingErrorKind Kind { get; }
}
