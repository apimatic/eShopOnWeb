using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by an eShopOnWeb shopper, as reported by the billing provider.
/// </summary>
/// <remarks>
/// Projected from the Maxio <c>Subscription</c> schema
/// (<c>maxio-spec/components/schemas/Subscription.yaml</c>). Maxio stays the system of record:
/// nothing here is persisted by eShopOnWeb.
/// </remarks>
public record CustomerSubscription
{
    /// <summary>Provider identifier of the subscription (Maxio <c>subscription.id</c>).</summary>
    public required int Id { get; init; }

    /// <summary>The reference eShopOnWeb assigned to the subscription at signup.</summary>
    public string? Reference { get; init; }

    public required SubscriptionState State { get; init; }

    /// <summary>Raw provider state, retained verbatim so unrecognised states stay diagnosable.</summary>
    public required string RawState { get; init; }

    public required string PlanHandle { get; init; }

    public required string PlanName { get; init; }

    /// <summary>Price of the plan version this subscription is bound to, in the smallest currency unit.</summary>
    public required long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    public required string Currency { get; init; }

    public required int Interval { get; init; }

    public required string IntervalUnit { get; init; }

    /// <summary>
    /// When the next renewal charge is scheduled. Taken from Maxio
    /// <c>subscription.next_assessment_at</c>, falling back to <c>current_period_ends_at</c>.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    public decimal Balance => BalanceInCents / 100m;

    /// <summary>How the provider collects payment, e.g. automatic or remittance.</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Provider identifier of the customer the subscription belongs to.</summary>
    public required int CustomerId { get; init; }
}
