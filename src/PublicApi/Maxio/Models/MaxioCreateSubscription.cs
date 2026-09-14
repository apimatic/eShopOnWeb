namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Request body for Create Subscription (POST /subscriptions.json).
/// Wire shape: <c>{ "subscription": { ... } }</c> per Create-Subscription-Request.yaml.
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    public MaxioCreateSubscription? Subscription { get; set; }
}

/// <summary>
/// The subscription attributes used to enroll an existing customer on a plan
/// (Create-Subscription.yaml). Only the fields this integration populates are surfaced.
/// </summary>
public class MaxioCreateSubscription
{
    public string? ProductHandle { get; set; }

    public string? CustomerReference { get; set; }

    public string? PaymentCollectionMethod { get; set; }
}
