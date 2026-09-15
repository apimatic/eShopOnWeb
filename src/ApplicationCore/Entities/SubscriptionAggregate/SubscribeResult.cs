namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }
    public required bool Created { get; init; }
}
