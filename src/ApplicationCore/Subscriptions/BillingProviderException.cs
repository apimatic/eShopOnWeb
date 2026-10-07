using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public enum BillingFailureKind
{
    Rejected,
    NotFound,
    Unavailable,
    Misconfigured
}

public class BillingProviderException : Exception
{
    public BillingProviderException(BillingFailureKind kind, string message, IReadOnlyList<string>? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Details = details ?? Array.Empty<string>();
    }

    public BillingFailureKind Kind { get; }

    public IReadOnlyList<string> Details { get; }
}

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' does not exist or is not available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
