using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionFailure
{
    /// <summary>The caller asked for something invalid (e.g. a plan the catalog does not offer).</summary>
    Invalid,
    /// <summary>The same operation is already in progress.</summary>
    Conflict,
    /// <summary>The billing system refused the request.</summary>
    Rejected,
    /// <summary>The billing system could not be reached or answered unusably.</summary>
    Unavailable
}

public class SubscriptionBillingException : Exception
{
    public SubscriptionFailure Failure { get; }

    public SubscriptionBillingException(SubscriptionFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }
}
