using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>The caller's request cannot be honoured (unknown plan, no usable e-mail ...). Message is caller-safe.</summary>
public class BillingRequestException : Exception
{
    public BillingRequestException(string message) : base(message) { }
}

public sealed class PlanNotOfferedException : BillingRequestException
{
    public PlanNotOfferedException() : base("The requested plan is not offered.") { }
}

/// <summary>The same subscribe request is already being processed.</summary>
public sealed class SubscriptionInProgressException : Exception
{
    public SubscriptionInProgressException() : base("A subscription request for this plan is already in progress.") { }
}

/// <summary>The billing provider refused the request with a definite 4xx (nothing was created).</summary>
public sealed class BillingProviderRejectedException : Exception
{
    public BillingProviderRejectedException(string message, Exception? inner = null) : base(message, inner) { }
}

public enum BillingFailureKind
{
    /// <summary>Our credentials/config are wrong, or the provider answered with something unreadable.</summary>
    ConfigFault,
    /// <summary>The provider is unreachable or throttling; nothing was created.</summary>
    Unavailable,
    Timeout,
    /// <summary>A write may or may not have landed and could not be settled by re-reading.</summary>
    OutcomeUnknown
}

public sealed class BillingProviderException : Exception
{
    public BillingFailureKind Kind { get; }

    public BillingProviderException(BillingFailureKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}
