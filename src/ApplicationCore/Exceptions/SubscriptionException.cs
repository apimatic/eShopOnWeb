using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionFailure
{
    /// <summary>The requested plan is not offered by the configured product family.</summary>
    PlanNotOffered,
    /// <summary>The same request is already being processed.</summary>
    InProgress,
    /// <summary>Billing provider rejected the request (caller can act on it).</summary>
    Rejected,
    /// <summary>Billing provider is unreachable or failed; nothing the caller did wrong.</summary>
    ProviderUnavailable,
    /// <summary>The write may or may not have reached the provider yet; retrying settles it.</summary>
    OutcomeUnknown
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
