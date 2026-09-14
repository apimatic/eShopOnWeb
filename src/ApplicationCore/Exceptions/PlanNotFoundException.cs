namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscribe request names a plan handle that does not exist in the
/// configured Maxio product family. Distinct from other billing failures so callers
/// can map it to a 400 Bad Request rather than a 502.
/// </summary>
public sealed class PlanNotFoundException : MaxioBillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
