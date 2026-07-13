using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    /// <summary>Omit to target the caller's own active subscription. Administrators may target any subscription.</summary>
    public int? SubscriptionId { get; set; }

    [Required]
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>True: apply now with proration. False: apply at next renewal, no proration.</summary>
    public bool ApplyImmediately { get; set; } = true;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CallerReference { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public bool CallerIsAdmin { get; set; }
}
