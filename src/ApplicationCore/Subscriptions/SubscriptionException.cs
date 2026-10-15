using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum SubscriptionFailure
{
    /// <summary>The caller's identity or email cannot be used to create a billing customer.</summary>
    InvalidUser,
    /// <summary>The requested plan is not one the configured product family offers.</summary>
    InvalidPlan,
    /// <summary>The billing system rejected the request for a reason the caller can act on.</summary>
    Rejected,
    /// <summary>Another request for the same subscription is in flight, or its outcome is still being settled.</summary>
    InProgress,
    /// <summary>The billing system is unreachable, slow, or refused our credentials.</summary>
    Unavailable
}

public class SubscriptionException : Exception
{
    public SubscriptionFailure Failure { get; }

    public SubscriptionException(SubscriptionFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }
}
