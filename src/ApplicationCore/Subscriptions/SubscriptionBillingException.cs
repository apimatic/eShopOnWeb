using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The single failure type the billing abstraction surfaces to callers, so the API layer has one
/// error shape to translate rather than the billing SDK's exception zoo. <see cref="Kind"/> lets
/// the caller decide an HTTP status without inspecting provider internals.
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingErrorKind Kind { get; }

    public SubscriptionBillingException(string message, SubscriptionBillingErrorKind kind, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }
}

public enum SubscriptionBillingErrorKind
{
    /// <summary>The caller sent something invalid (e.g. an unknown plan). Maps to 400.</summary>
    InvalidRequest,

    /// <summary>A requested resource does not exist. Maps to 404.</summary>
    NotFound,

    /// <summary>
    /// The billing provider is unreachable, rejected our credentials, throttled us, or answered
    /// with an unusable body — none of which the caller caused or can fix. Maps to 502/503.
    /// </summary>
    ProviderUnavailable,

    /// <summary>An unclassified failure. Maps to 500/502.</summary>
    Unexpected
}
