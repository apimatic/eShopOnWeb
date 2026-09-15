namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscribeToPlan
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string UserName { get; init; }
    public required string ProductHandle { get; init; }
}

public sealed record SubscribeResult
{
    public required CustomerSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
