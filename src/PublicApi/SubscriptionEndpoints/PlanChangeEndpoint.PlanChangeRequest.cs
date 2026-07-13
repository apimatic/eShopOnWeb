using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>Omit to target the caller's own active subscription. Administrators may target any subscription.</summary>
    public int? SubscriptionId { get; set; }

    [Required]
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>The exact preview the customer confirmed - re-verified against a fresh preview before committing.</summary>
    [Required]
    public PlanChangePreviewDto ConfirmedPreview { get; set; } = new PlanChangePreviewDto();

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CallerReference { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public bool CallerIsAdmin { get; set; }
}
