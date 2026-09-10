namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The outcome of a <see cref="SubscribeCommand"/>. <see cref="AlreadyExisted"/> is true
/// when an equivalent live subscription was already present, in which case that existing
/// subscription is returned unchanged (the operation is idempotent).
/// </summary>
public record SubscribeResult(
    CustomerSubscription Subscription,
    bool AlreadyExisted,
    int CustomerId,
    bool CustomerCreated);
