namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle is not one of the plans available in the configured product family.
/// Enforces the cross-operation invariant that a subscribe target must be a plan the plan list
/// returns. Surfaces to the caller as a 400.
/// </summary>
public class PlanNotFoundException : SubscriptionBillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.", statusCode: 400)
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
