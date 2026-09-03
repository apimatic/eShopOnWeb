using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan handle is not one of the plans available in the configured product family.
/// A caller mistake, surfaced as 400 Bad Request.
/// </summary>
public sealed class PlanNotFoundException : BillingException
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.", (int)HttpStatusCode.BadRequest)
    {
    }
}
