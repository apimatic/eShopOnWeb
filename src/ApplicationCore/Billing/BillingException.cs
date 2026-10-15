using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum BillingFailure
{
    /// <summary>The requested plan is not one this shop offers.</summary>
    PlanNotFound,
    /// <summary>Another subscribe for the same user and plan is still in flight.</summary>
    InProgress,
    /// <summary>The billing provider rejected the request itself.</summary>
    Rejected,
    /// <summary>The billing provider could not be used (credentials, quota, outage, unreadable reply).</summary>
    ProviderUnavailable,
    /// <summary>A write may have reached the billing provider but could not be confirmed.</summary>
    OutcomeUnknown
}

public class BillingException : Exception
{
    public BillingFailure Failure { get; }

    public BillingException(BillingFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }
}
