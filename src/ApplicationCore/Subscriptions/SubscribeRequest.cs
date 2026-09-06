namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Enrolls <paramref name="Subscriber"/> in the plan identified by <paramref name="PlanHandle"/>.
/// </summary>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key. Repeating a request with the same key cannot create a second
/// subscription; omitting it is still safe, because the implementation reconciles against the
/// provider before enrolling.
/// </param>
public sealed record SubscribeRequest(
    SubscriberIdentity Subscriber,
    string PlanHandle,
    string? IdempotencyKey = null);
