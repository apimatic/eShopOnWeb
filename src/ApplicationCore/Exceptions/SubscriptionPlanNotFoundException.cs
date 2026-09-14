namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested plan handle is not offered by the configured product family.</summary>
public sealed class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.", 404)
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
