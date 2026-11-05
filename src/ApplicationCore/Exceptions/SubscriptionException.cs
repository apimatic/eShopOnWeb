using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionFailure
{
    /// <summary>The requested plan is not offered.</summary>
    InvalidPlan,
    /// <summary>The billing system refused the request; the caller can act on the message.</summary>
    Rejected,
    /// <summary>The plan list could not be confirmed (truncated); try again later.</summary>
    Unconfirmed,
    /// <summary>The billing system could not be reached or answered unusably.</summary>
    Unavailable,
    /// <summary>The billing system did not answer in time.</summary>
    Timeout
}

public class SubscriptionException : Exception
{
    public SubscriptionException(SubscriptionFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public SubscriptionFailure Failure { get; }
}
