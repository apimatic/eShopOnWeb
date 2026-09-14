namespace Microsoft.eShopWeb.MaxioBilling.Models;

/// <summary>A subscription belonging to an eShopOnWeb user, as Maxio holds it.</summary>
public sealed record SubscriptionSummary
{
    public int? Id { get; init; }

    /// <summary>Maxio subscription state as reported on the wire, e.g. <c>active</c>.</summary>
    public string? State { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Price captured at signup, in minor units.</summary>
    public long? PriceInCents { get; init; }

    /// <summary>Amount the next invoice will carry, in minor units.</summary>
    public long? CurrentBillingAmountInCents { get; init; }

    public string? Currency { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>The next billing date. Null while the subscription is not being assessed.</summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }

    public int? CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    /// <summary>The reference eShopOnWeb stamped on the subscription, for traceability.</summary>
    public string? Reference { get; init; }
}
