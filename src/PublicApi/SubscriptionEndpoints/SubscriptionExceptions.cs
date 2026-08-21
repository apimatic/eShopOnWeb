using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured Maxio product family.") { }
}

public sealed class SubscriptionEnrollmentInProgressException : Exception
{
    public SubscriptionEnrollmentInProgressException()
        : base("This subscription enrollment is already being processed. Retry shortly.") { }
}

public sealed class ShopperNotFoundException : Exception
{
    public ShopperNotFoundException() : base("The authenticated shopper no longer exists.") { }
}

public sealed class SubscriptionOwnershipException : Exception
{
    public SubscriptionOwnershipException()
        : base("Maxio returned a subscription that does not belong to the authenticated shopper.") { }
}
