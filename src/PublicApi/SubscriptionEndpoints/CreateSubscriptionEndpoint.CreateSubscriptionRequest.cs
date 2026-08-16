using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to enroll in (e.g. the Pro plan's handle). Required — the target plan is
    /// never assumed, so the same build works against any catalog.
    /// </summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;
}
