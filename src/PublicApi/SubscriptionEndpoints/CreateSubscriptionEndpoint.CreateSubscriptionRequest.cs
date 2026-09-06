using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// Handles are stable across catalog re-seeds; numeric plan ids are not.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "planHandle is required.")]
    public string PlanHandle { get; set; } = string.Empty;
}
