using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionFailure
{
    /// <summary>The requested plan is not offered.</summary>
    PlanNotFound,
    /// <summary>The same subscribe request is still being processed.</summary>
    InProgress,
    /// <summary>Maxio rejected the request (4xx the caller can act on).</summary>
    Rejected,
    /// <summary>Billing provider unavailable, misconfigured, or answered unusably.</summary>
    ProviderUnavailable
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
