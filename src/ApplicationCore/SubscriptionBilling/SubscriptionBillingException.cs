using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Categorizes why a subscription-billing operation failed so the API layer can
/// translate it into an appropriate HTTP status without leaking provider details.
/// </summary>
public enum SubscriptionBillingErrorKind
{
    /// <summary>Billing integration settings are missing or invalid.</summary>
    Configuration,

    /// <summary>The billing provider rejected our credentials.</summary>
    Unauthorized,

    /// <summary>The requested plan does not exist in the configured catalog.</summary>
    PlanNotFound,

    /// <summary>The billing provider refused the request (validation error).</summary>
    Rejected,

    /// <summary>A duplicate submission was prevented.</summary>
    Duplicate,

    /// <summary>The provider reported a resource that no longer exists.</summary>
    ResourceNotFound,

    /// <summary>The billing provider could not be reached.</summary>
    Unavailable,
}

/// <summary>
/// Thrown when an interaction with the billing system of record fails.
/// Messages must never contain credentials or other secret values.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingErrorKind Kind { get; }

    /// <summary>Field-level errors reported by the billing provider, when available.</summary>
    public IReadOnlyList<string> Errors { get; }

    public SubscriptionBillingException(SubscriptionBillingErrorKind kind, string message, IReadOnlyList<string>? errors = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Errors = errors ?? Array.Empty<string>();
    }
}
