namespace Microsoft.eShopWeb.ApplicationCore.Billing.Exceptions;

/// <summary>
/// The caller asked for a plan handle that is not offered by the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : BillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    /// <summary>The handle that could not be resolved.</summary>
    public string PlanHandle { get; }
}
