namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription plan handle does not exist in the configured
/// Maxio product family.
/// </summary>
public class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
    }
}
