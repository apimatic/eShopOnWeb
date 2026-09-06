using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Command to enrol a <see cref="Subscriber"/> in the plan identified by <paramref name="PlanHandle"/>.
/// </summary>
/// <param name="Subscriber">The shopper to enrol.</param>
/// <param name="PlanHandle">API handle of the target plan, as returned by the plan listing.</param>
/// <param name="IdempotencyKey">
/// Optional caller-supplied key. When supplied, repeated calls with the same key and shopper always
/// resolve to the same subscription. When omitted, the plan handle itself scopes idempotency, so a
/// double-click on "Subscribe" cannot produce two enrolments in the same plan.
/// </param>
public record SubscribeRequest(Subscriber Subscriber, string PlanHandle, string? IdempotencyKey = null)
{
    public string PlanHandle { get; } = Guard.Against.NullOrWhiteSpace(PlanHandle, nameof(PlanHandle));
}
