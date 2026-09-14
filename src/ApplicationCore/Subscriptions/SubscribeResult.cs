namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Outcome of a subscribe request.
/// </summary>
/// <param name="Subscription">The subscription the shopper now holds.</param>
/// <param name="Created">
/// <c>true</c> when this call created the subscription; <c>false</c> when an equivalent
/// subscription already existed and was returned instead.
/// </param>
/// <param name="CustomerCreated"><c>true</c> when this call created the billing customer.</param>
/// <param name="CustomerReference">The provider customer reference used for the shopper.</param>
public record SubscribeResult(
    CustomerSubscription Subscription,
    bool Created,
    bool CustomerCreated,
    string CustomerReference);
