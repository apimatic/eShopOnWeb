using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Why a billing operation failed. The kind — not the provider's status alone — decides how the
/// failure is presented to the caller, because a lost response and a rejected request are different
/// facts even when both leave us without a subscription.
/// </summary>
public enum BillingFailureKind
{
    /// <summary>The provider deliberately rejected the request (4xx). The caller can act on it; retrying as-is will not help.</summary>
    ProviderRejected = 0,

    /// <summary>The provider was unreachable, errored (5xx), or answered something unusable. Retrying later may help.</summary>
    ProviderUnavailable = 1,

    /// <summary>
    /// A write may or may not have taken effect and we could not establish which. Never report this as a
    /// plain failure: the caller must re-read state rather than blindly retry.
    /// </summary>
    OutcomeUnknown = 2,

    /// <summary>The billing integration is not configured on this host, so no call was attempted.</summary>
    NotConfigured = 3,

    /// <summary>The call exceeded its budget or the caller went away.</summary>
    Timeout = 4
}

/// <summary>
/// The single failure type the billing abstraction raises. Carries a caller-safe message and, where
/// one was readable, the provider's HTTP status so distinct failures stay distinct at the API boundary.
/// Provider exception text is never propagated into <see cref="Exception.Message"/>.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message, BillingFailureKind kind, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public BillingFailureKind Kind { get; }

    /// <summary>The provider's HTTP status, when one could be read off the failure. Null otherwise.</summary>
    public int? ProviderStatusCode { get; }
}
