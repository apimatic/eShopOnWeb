namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// Request to enroll a shopper on a plan.
/// </summary>
public sealed record SubscribeCommand
{
    /// <summary>Who is subscribing.</summary>
    public required SubscriberIdentity Subscriber { get; init; }

    /// <summary>Handle of the plan to enroll on.</summary>
    public required string PlanHandle { get; init; }

    /// <summary>Optional price point handle, when the plan exposes more than one.</summary>
    public string? PricePointHandle { get; init; }

    /// <summary>
    /// Optional caller-supplied idempotency key. Replaying the same key for the same subscriber
    /// returns the subscription created by the first call instead of creating another one.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
