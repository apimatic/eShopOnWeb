namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Enrolls a shopper onto the plan identified by its handle.
/// </summary>
/// <param name="Subscriber">The authenticated shopper.</param>
/// <param name="PlanHandle">Handle of a plan in the configured product family.</param>
/// <param name="PaymentCollectionMethod">
/// Optional override of how the subscription is collected. Defaults to a method that does not
/// require a card on file.
/// </param>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key identifying one subscribe intent. Two requests carrying the same
/// key are treated as the same attempt, however far apart they arrive. When omitted, retries are
/// recognised for a short window instead.
/// </param>
public sealed record SubscribeRequest(
    SubscriberIdentity Subscriber,
    string PlanHandle,
    string? PaymentCollectionMethod = null,
    string? IdempotencyKey = null);
