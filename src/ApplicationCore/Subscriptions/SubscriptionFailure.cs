namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Why a subscription operation could not be completed. Kept transport agnostic; the API
/// layer decides which HTTP status each value maps to.
/// </summary>
public enum SubscriptionFailure
{
    None = 0,

    /// <summary>The billing system is not configured for this deployment.</summary>
    NotConfigured,

    /// <summary>The caller sent something the application rejected before calling the billing system.</summary>
    InvalidRequest,

    /// <summary>The requested plan is not offered by the configured product family.</summary>
    PlanNotFound,

    /// <summary>The billing system reported a duplicate in-flight request.</summary>
    Conflict,

    /// <summary>The billing system understood the request and refused it (for example, validation).</summary>
    UpstreamRejected,

    /// <summary>The billing system could not be reached, or failed in a way that may be transient.</summary>
    UpstreamUnavailable
}
