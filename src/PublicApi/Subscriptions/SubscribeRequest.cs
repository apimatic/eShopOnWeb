using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest : BaseRequest
{
    [Required]
    public string PlanHandle { get; init; } = string.Empty;
}
