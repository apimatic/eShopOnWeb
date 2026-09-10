namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse
{
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// True when the shopper already had a live subscription to this plan and it was returned
    /// unchanged (idempotent) rather than a new one being created.
    /// </summary>
    public bool AlreadyActive { get; set; }

    public string Message { get; set; } = string.Empty;
}
