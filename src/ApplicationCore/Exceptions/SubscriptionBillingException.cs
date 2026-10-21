using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionBillingError
{
    /// <summary>The plan handle is not one this shop offers.</summary>
    UnknownPlan,

    /// <summary>Another request is working on this user's signup for this plan.</summary>
    SignupInProgress,

    /// <summary>The billing system rejected the signup.</summary>
    Rejected,

    /// <summary>The billing system asked us to slow down.</summary>
    Throttled,

    /// <summary>The billing system could not be reached or answered with an error.</summary>
    ProviderUnavailable,

    /// <summary>Writes are disabled for this configuration (e.g. a live site in Development).</summary>
    WritesDisabled,
}

/// <summary>
/// A subscription request that cannot be served. The message is safe to show the caller: it never
/// carries the billing system's raw response.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(SubscriptionBillingError error, string message,
        TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        RetryAfter = retryAfter;
    }

    public SubscriptionBillingError Error { get; }

    public TimeSpan? RetryAfter { get; }
}
