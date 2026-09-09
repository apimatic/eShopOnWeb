namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Input to the subscribe flow. <see cref="PlanHandle"/> is the plan the shopper chose (from
/// <c>GET /api/subscription-plans</c>). When omitted, the service falls back to the configured
/// default plan handle, or — if none is configured — the first plan in the product family, so the
/// build stays catalog-agnostic.
/// </summary>
public record SubscribeRequest
{
    public string? PlanHandle { get; init; }
}
