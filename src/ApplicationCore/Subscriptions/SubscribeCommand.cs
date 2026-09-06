using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Request to enroll a shopper on a plan.
/// </summary>
public class SubscribeCommand
{
    public SubscribeCommand(BillingIdentity identity, string planHandle, string? idempotencyKey = null)
    {
        Identity = Guard.Against.Null(identity, nameof(identity));
        PlanHandle = Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle)).Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey!.Trim();
    }

    public BillingIdentity Identity { get; }

    /// <summary>Handle of the plan to enroll on.</summary>
    public string PlanHandle { get; }

    /// <summary>
    /// Optional caller-supplied key. Two requests carrying the same key for the same shopper
    /// resolve to the same subscription instead of creating a second one. When omitted a key is
    /// derived from the shopper and the plan, so a double-click is safe by default.
    /// </summary>
    public string? IdempotencyKey { get; }
}
