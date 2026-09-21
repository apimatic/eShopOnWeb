using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the subscription-billing integration surfaces to callers. The Maxio SDK's
/// two error families (typed <c>SdkException&lt;{Operation}Error&gt;</c> and raw
/// <c>SdkException&lt;RawError&gt;</c>), transport failures, and malformed-body <c>JsonException</c>s are all
/// translated into this at the integration boundary so callers reason about one type, with a caller-safe
/// message only (no SDK/type detail is ever surfaced on the wire).
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingErrorKind Kind { get; }

    /// <summary>The provider HTTP status, when one was available (Case B / status-carrying errors). Null otherwise.</summary>
    public int? ProviderStatusCode { get; }

    public SubscriptionBillingException(
        SubscriptionBillingErrorKind kind,
        string message,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }
}

/// <summary>
/// Classifies a billing failure so the HTTP boundary can pick a coherent status. Note the deliberate
/// split: a provider auth/rate-limit failure is <see cref="ProviderUnavailable"/> (our fault, not the
/// caller's), while validation/not-found/conflict are the caller's to act on.
/// </summary>
public enum SubscriptionBillingErrorKind
{
    /// <summary>The caller's request was rejected by the provider as invalid (e.g. 422). → 400.</summary>
    InvalidRequest,

    /// <summary>A referenced resource does not exist. → 404.</summary>
    NotFound,

    /// <summary>A conflicting concurrent write that could not be reconciled. → 409.</summary>
    Conflict,

    /// <summary>The provider is unreachable, timed out, rate-limited us, rejected our credentials, or returned an unreadable/5xx response. → 502.</summary>
    ProviderUnavailable,

    /// <summary>An unmapped/unexpected failure. → 500.</summary>
    Unknown
}
