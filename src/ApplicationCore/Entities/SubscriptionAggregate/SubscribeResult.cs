namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscribeResult
{
    public BillingSubscription Subscription { get; init; } = null!;
    public bool Created { get; init; }
}
