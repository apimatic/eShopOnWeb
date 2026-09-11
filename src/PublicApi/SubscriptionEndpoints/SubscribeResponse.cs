using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public DateTimeOffset? NextBilling { get; set; }
    public string CustomerReference { get; set; } = "";
}
