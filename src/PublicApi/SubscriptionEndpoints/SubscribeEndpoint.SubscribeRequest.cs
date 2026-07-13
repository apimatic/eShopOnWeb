using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    [Required]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Set from the authenticated caller's identity in AddRoute; never client-supplied.</summary>
    public string CustomerEmail { get; set; } = string.Empty;
}
