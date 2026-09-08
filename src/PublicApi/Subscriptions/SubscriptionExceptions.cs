using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available in the configured Maxio product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}

public class BillingAccountNotFoundException : Exception
{
    public BillingAccountNotFoundException()
        : base("No billing account could be found or created for the current user.")
    {
    }
}
