namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper attempts to subscribe to a plan that does not exist in the
/// configured product family.
/// </summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
