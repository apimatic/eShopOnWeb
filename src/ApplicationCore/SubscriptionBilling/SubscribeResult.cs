using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// Confirmation returned to the shopper after subscribing: the plan, price, subscription state, and the
/// next billing date, plus whether the subscription was created now or already existed.
/// </summary>
public record SubscribeResult
{
    public required SubscribeOutcome Outcome { get; init; }
    public required string PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long PriceInCents { get; init; }
    public required string FormattedPrice { get; init; }

    /// <summary>The Maxio subscription state wire value, e.g. <c>active</c>.</summary>
    public string? State { get; init; }

    /// <summary>When the next regularly scheduled charge is due (Maxio current_period_ends_at / next_assessment_at).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public int SubscriptionId { get; init; }
    public int CustomerId { get; init; }
    public required string Reference { get; init; }
}
