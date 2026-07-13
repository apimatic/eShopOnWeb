using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    /// <summary>Omit to record against the caller's own active subscription. Administrators may target any subscription.</summary>
    public int? SubscriptionId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CallerReference { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public bool CallerIsAdmin { get; set; }
}
