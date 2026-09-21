using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Classifies a billing failure so the API boundary can pick a coherent HTTP status.</summary>
public enum SubscriptionBillingErrorKind
{
    /// <summary>The caller's request was rejected (e.g. unknown plan, provider validation 4xx) — a 4xx.</summary>
    InvalidRequest,

    /// <summary>Our credentials/quota, a provider 5xx, or the provider was unreachable — a 5xx; the caller cannot fix it.</summary>
    ProviderUnavailable,

    /// <summary>An unexpected/unrecognized failure — a 5xx.</summary>
    Unexpected
}

/// <summary>
/// The single failure type the billing integration surfaces to its callers. It never carries raw SDK or
/// JSON internals — only a caller-safe message plus a classification.
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingErrorKind Kind { get; }

    /// <summary>The provider's HTTP status, when one was available.</summary>
    public int? ProviderStatusCode { get; }

    public SubscriptionBillingException(
        string message,
        SubscriptionBillingErrorKind kind,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }
}
