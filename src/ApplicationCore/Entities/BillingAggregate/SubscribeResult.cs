namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// Outcome of a subscribe request. <see cref="AlreadyExisted"/> is <c>true</c> when the shopper
/// was already enrolled in the requested plan and the existing subscription was returned instead
/// of creating a duplicate (the idempotent path a double-submit lands on).
/// </summary>
public sealed record SubscribeResult
{
    public required CustomerSubscriptionInfo Subscription { get; init; }

    public bool AlreadyExisted { get; init; }
}
