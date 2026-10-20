using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotOfferedException : Exception
{
    public SubscriptionPlanNotOfferedException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
