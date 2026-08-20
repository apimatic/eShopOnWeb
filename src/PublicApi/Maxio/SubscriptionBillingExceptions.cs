using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public abstract class SubscriptionBillingException : Exception
{
    protected SubscriptionBillingException(string message) : base(message) { }
}

public sealed class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.") { }
}

public sealed class SubscriptionPlanRequiresPaymentException : SubscriptionBillingException
{
    public SubscriptionPlanRequiresPaymentException(string productHandle)
        : base($"Subscription plan '{productHandle}' requires a payment method, which this endpoint does not collect.") { }
}

public sealed class SubscriptionCreationInProgressException : SubscriptionBillingException
{
    public SubscriptionCreationInProgressException()
        : base("A subscription request for this plan is already in progress. Retry shortly to retrieve its result.") { }
}
