using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle) : base($"No subscription plan found with handle '{planHandle}'")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}