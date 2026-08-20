using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    [Required]
    public string ProductHandle { get; set; } = string.Empty;
}
