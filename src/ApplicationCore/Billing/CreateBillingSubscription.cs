namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CreateBillingSubscription(
    string ProductHandle,
    string? PricePointHandle,
    string CustomerReference,
    string Reference);

public sealed record SubscribeResult(bool Created, SubscriptionDetails Subscription);
