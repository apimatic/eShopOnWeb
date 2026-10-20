using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionBillingFailure
{
    /// <summary>The caller's request is invalid (e.g. an unknown plan).</summary>
    InvalidRequest,

    /// <summary>The billing provider rejected the caller's request; <see cref="SubscriptionBillingException.Details"/> carries its reasons.</summary>
    Rejected,

    /// <summary>The same operation is already in progress for this caller.</summary>
    InProgress,

    /// <summary>A write may have reached the billing provider but could not be confirmed; retrying is safe.</summary>
    OutcomeUnknown,

    /// <summary>The billing provider is throttling requests.</summary>
    ProviderBusy,

    /// <summary>The billing provider failed, was unreachable, or rejected this application's own configuration.</summary>
    ProviderUnavailable
}

/// <summary>
/// A subscription-billing failure carrying only caller-safe text.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(SubscriptionBillingFailure failure, string message,
        IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        Details = details ?? Array.Empty<string>();
    }

    public SubscriptionBillingFailure Failure { get; }

    public IReadOnlyList<string> Details { get; }
}
