using System;

namespace Microsoft.eShopWeb.PublicApi.Models;

public class UserSubscription
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionState { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
