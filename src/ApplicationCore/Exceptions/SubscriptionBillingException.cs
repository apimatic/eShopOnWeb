using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum SubscriptionBillingErrorKind
{
    /// <summary>The requested plan is not one this store offers.</summary>
    PlanNotOffered,

    /// <summary>The billing provider rejected the request; <see cref="SubscriptionBillingException.Details"/> carries its reasons.</summary>
    Rejected,

    /// <summary>The user already holds a live subscription to a different plan.</summary>
    AlreadySubscribed,

    /// <summary>Another subscribe attempt for the same user is in flight.</summary>
    InProgress,

    /// <summary>The billing provider could not be reached or answered too slowly; safe to retry later.</summary>
    Unavailable,

    /// <summary>The billing provider failed or is misconfigured on our side; not something the caller can fix.</summary>
    ProviderError
}

/// <summary>
/// A subscription billing failure carrying a caller-safe message. Provider internals stay in the inner exception and logs.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(SubscriptionBillingErrorKind kind, string message,
        IReadOnlyList<string>? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Details = details ?? Array.Empty<string>();
    }

    public SubscriptionBillingErrorKind Kind { get; }

    public IReadOnlyList<string> Details { get; }
}
