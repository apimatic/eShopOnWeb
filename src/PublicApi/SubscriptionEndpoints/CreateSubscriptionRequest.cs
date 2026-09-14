using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionRequest
{
    [Required]
    [MaxLength(255)]
    public string ProductHandle { get; init; } = string.Empty;
}
