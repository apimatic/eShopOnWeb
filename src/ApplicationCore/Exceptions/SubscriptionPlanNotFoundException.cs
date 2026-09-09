namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper attempts to subscribe to a plan handle that does not exist in the
/// configured Maxio product family. Endpoints translate this into a 404 response.
/// </summary>
public class SubscriptionPlanNotFoundException : SubscriptionBillingException
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan found with handle '{planHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
