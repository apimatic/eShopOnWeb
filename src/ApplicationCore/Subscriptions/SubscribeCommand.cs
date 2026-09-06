namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Request to enroll a shopper onto a plan.
/// </summary>
/// <param name="Subscriber">The eShopOnWeb user being enrolled.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to.</param>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key. When supplied, replaying the same key for the same subscriber and
/// plan always resolves to the same subscription instead of creating another one, even after the
/// original subscription has been canceled.
/// </param>
public record SubscribeCommand(
    SubscriberProfile Subscriber,
    string PlanHandle,
    string? IdempotencyKey = null);
