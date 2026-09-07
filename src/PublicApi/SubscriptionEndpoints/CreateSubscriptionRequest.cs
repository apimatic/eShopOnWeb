using MaxioAdvancedBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public int? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public CreateSubscription? Subscription { get; set; }
}
