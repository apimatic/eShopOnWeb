using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Taken from the bearer token, not from the caller's payload.
    /// </summary>
    internal string? UserName { get; set; }
}
