namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan is not part of the configured product catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle, string availablePlans)
        : base($"Subscription plan '{planHandle}' was not found. Available plans: {availablePlans}.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
