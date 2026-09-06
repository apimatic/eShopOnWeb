namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan handle does not exist in the configured catalog.</summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
