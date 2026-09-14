using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.")
    {
    }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("A subscription enrollment for this plan is already in progress. Retry the account read shortly.")
    {
    }
}

