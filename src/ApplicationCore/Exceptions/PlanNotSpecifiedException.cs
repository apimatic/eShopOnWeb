namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscribe request named no plan and the deployment has no default plan configured, so there is
/// nothing to enroll the shopper on. Surfaced to callers as <c>400 Bad Request</c>.
/// </summary>
public class PlanNotSpecifiedException : BillingException
{
    public PlanNotSpecifiedException()
        : base("No plan was specified and no default plan is configured. Pass a planHandle from GET /api/subscription-plans, or configure Maxio:DefaultPlanHandle.")
    {
    }
}
