using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>How a billing failure should be presented to the caller (kept distinct so the boundary can
/// pick the right HTTP status without leaking provider/SDK detail).</summary>
public enum BillingErrorKind
{
    /// <summary>The caller's request was rejected by the provider (e.g. 422 validation). → 400.</summary>
    InvalidRequest,

    /// <summary>The requested plan is not one of the configured family's plans. → 404.</summary>
    PlanNotFound,

    /// <summary>Our credentials/quota, a transport failure, or a provider 5xx — the caller can't fix it. → 502.</summary>
    ProviderUnavailable,

    /// <summary>An unmapped or ambiguous outcome. → 502.</summary>
    Unknown
}

/// <summary>
/// The single failure type the billing service surfaces at its boundary. Carries a caller-safe message
/// only — SDK/provider exception text is logged, never propagated onto the wire.
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    public BillingErrorKind Kind { get; }

    public SubscriptionBillingException(BillingErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException) => Kind = kind;
}
