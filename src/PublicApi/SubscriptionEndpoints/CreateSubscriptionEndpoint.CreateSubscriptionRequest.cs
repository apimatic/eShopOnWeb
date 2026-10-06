namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for subscribing to a plan. When ProductHandle is omitted, the
/// configured default plan is used.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string? ProductHandle { get; set; }
}