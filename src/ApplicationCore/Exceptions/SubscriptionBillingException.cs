using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Why a subscription-billing call failed. The API layer maps each kind to one HTTP status.</summary>
public enum SubscriptionBillingErrorKind
{
    /// <summary>The requested plan is not one this store offers.</summary>
    UnknownPlan,

    /// <summary>The subscriber already holds a subscription to a different plan.</summary>
    AlreadySubscribedToOtherPlan,

    /// <summary>A subscribe call for this subscriber is already in progress.</summary>
    SubscriptionInProgress,

    /// <summary>The billing provider rejected the request; <see cref="SubscriptionBillingException.ProviderErrors"/> carries its messages.</summary>
    RejectedByProvider,

    /// <summary>The plan catalog could not be read completely, so the requested plan could not be verified.</summary>
    CatalogIncomplete,

    /// <summary>The provider answered with an error that is not the caller's to fix (credentials, configuration, 5xx, unreadable body).</summary>
    ProviderError,

    /// <summary>The provider could not be reached, or is throttling us.</summary>
    ProviderUnavailable,

    /// <summary>The provider did not answer within the time budget.</summary>
    ProviderTimeout
}

/// <summary>A caller-safe subscription-billing failure. Its message never carries provider internals or secrets.</summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(
        SubscriptionBillingErrorKind kind,
        string message,
        IReadOnlyList<string>? providerErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderErrors = providerErrors ?? Array.Empty<string>();
    }

    public SubscriptionBillingErrorKind Kind { get; }

    /// <summary>Validation messages returned by the billing provider, when it rejected the request.</summary>
    public IReadOnlyList<string> ProviderErrors { get; }
}
