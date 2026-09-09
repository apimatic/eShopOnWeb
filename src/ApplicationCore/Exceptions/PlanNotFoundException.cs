namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to subscribe to a plan handle that does not exist in the configured
/// product family. Surfaced to callers as a 404 (unknown plan) rather than a 500.
/// </summary>
public sealed class PlanNotFoundException : SubscriptionBillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' was found.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
