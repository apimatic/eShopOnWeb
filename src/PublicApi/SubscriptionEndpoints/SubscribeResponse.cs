namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when an existing subscription was returned instead of creating a new one (idempotent).</summary>
    public bool AlreadyExisted { get; set; }
}
