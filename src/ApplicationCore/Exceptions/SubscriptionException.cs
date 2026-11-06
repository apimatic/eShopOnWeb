using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionFailure
{
    /// <summary>The caller's request cannot be fulfilled as sent (unknown plan, no email on file, provider rejection).</summary>
    Invalid,
    /// <summary>A subscribe for this plan is already in progress.</summary>
    InProgress,
    /// <summary>The billing provider could not be reached or answered unusably; nothing the caller can fix.</summary>
    Unavailable,
    /// <summary>The write may or may not have landed and could not be settled yet.</summary>
    Pending
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
