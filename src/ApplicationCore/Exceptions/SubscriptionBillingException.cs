using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// How a subscription-billing failure should be surfaced to the caller. Distinguishes the caller's fault
/// (they can fix it) from ours/the provider's (they cannot), so the HTTP boundary maps each coherently.
/// </summary>
public enum SubscriptionBillingErrorKind
{
    /// <summary>The caller sent something invalid we can reject up front (e.g. an unknown plan handle).</summary>
    InvalidRequest,

    /// <summary>The provider rejected the caller's input (e.g. a 422 validation error).</summary>
    Validation,

    /// <summary>Our credentials/quota, a transport failure, or a provider 5xx — not the caller's fault.</summary>
    ProviderUnavailable
}

/// <summary>
/// Domain-level failure of a subscription-billing operation. Carries a caller-safe message only — never
/// the underlying SDK/provider exception text — plus a <see cref="Kind"/> the boundary maps to a status.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, SubscriptionBillingErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public SubscriptionBillingErrorKind Kind { get; }
}
