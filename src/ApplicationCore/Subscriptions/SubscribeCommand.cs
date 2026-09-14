namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Request to enroll a <see cref="Subscriber"/> in a plan.
/// </summary>
/// <param name="Subscriber">The eShopOnWeb user being enrolled.</param>
/// <param name="PlanHandle">Handle of the plan to subscribe to.</param>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key. When present it is recorded as the subscription's reference in the
/// billing system, so a retry of the same request resolves to the subscription the first attempt
/// created instead of enrolling the shopper twice.
/// </param>
public sealed record SubscribeCommand(
    Subscriber Subscriber,
    string PlanHandle,
    string? IdempotencyKey = null);
