using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested subscription plan handle does not exist in the configured Maxio product
/// family. Surfaces as a client 404 at the API boundary.
/// </summary>
public class PlanNotFoundException : SubscriptionBillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.", HttpStatusCode.NotFound)
    {
    }
}
