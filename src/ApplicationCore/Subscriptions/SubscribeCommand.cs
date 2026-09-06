namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Request to enroll a <see cref="Subscriber"/> on a plan.
/// </summary>
/// <param name="Subscriber">Who is subscribing.</param>
/// <param name="PlanHandle">Handle of the plan to enroll on, as returned by the plan listing.</param>
/// <param name="PricePointHandle">Optional non-default price point for the plan.</param>
/// <param name="IdempotencyKey">
/// Optional caller supplied key. When present it is stored as the subscription's provider-side reference,
/// which is unique per site, so a replayed request returns the original subscription instead of creating a second one.
/// </param>
public sealed record SubscribeCommand(
    Subscriber Subscriber,
    string PlanHandle,
    string? PricePointHandle = null,
    string? IdempotencyKey = null);
