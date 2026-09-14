using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionRequest : BaseRequest
{
    [Required]
    [MaxLength(255)]
    public string ProductHandle { get; set; } = string.Empty;
}
