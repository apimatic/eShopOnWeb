using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum SubscriptionFailure
{
    InvalidPlan,          // 400 planHandle missing
    UnknownPlan,          // 422
    InvalidIdentity,      // 422 token name not usable as customer email
    InProgress,           // 409 + Retry-After
    ProviderRejected,     // 422 with sanitized provider message
    BillingMisconfigured, // 502
    CatalogUnavailable,   // 503
    Unavailable,          // 503
    Timeout,              // 504
    BadGateway            // 502
}

/// <summary>Domain failure of the subscription service; mapped to an HTTP status in the API's exception middleware.</summary>
public class SubscriptionException : Exception
{
    public SubscriptionFailure Failure { get; }
    public int? ProviderStatus { get; }
    public TimeSpan? RetryAfter { get; }
    /// <summary>True when a provider write may have landed although the call failed (transport failure, timeout, unreadable 2xx, 5xx).</summary>
    public bool OutcomeUnknown { get; init; }

    public SubscriptionException(SubscriptionFailure failure, string message, int? providerStatus = null,
        TimeSpan? retryAfter = null, Exception? inner = null) : base(message, inner)
    {
        Failure = failure;
        ProviderStatus = providerStatus;
        RetryAfter = retryAfter;
    }
}
