using System;

namespace Microsoft.eShopWeb.PublicApi.Services.Subscriptions;

public class UnknownPlanException : Exception
{
    public UnknownPlanException(string planHandle)
        : base($"The plan '{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
