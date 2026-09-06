using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// <para>
/// Projected from a Maxio Advanced Billing <c>Product</c>
/// (OpenAPI schema <c>components/schemas/Product.yaml</c>). Maxio is the system of record:
/// nothing about a plan is persisted locally.
/// </para>
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (Maxio <c>product.handle</c>). This is the identifier callers subscribe with.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Maxio's numeric product id. Not stable across catalog re-seeds - prefer <see cref="Handle"/>.</summary>
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the site's minor currency unit (Maxio <c>product.price_in_cents</c>).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code of the Maxio site (Maxio <c>site.currency</c>), when it could be resolved.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (Maxio <c>product.interval</c>).</summary>
    public int Interval { get; init; }

    /// <summary>Renewal interval unit - <c>day</c> or <c>month</c> (Maxio <c>product.interval_unit</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Maxio <c>product.require_credit_card</c>: whether a payment profile must exist before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Maxio <c>product.trial_interval</c>, when the plan has a trial.</summary>
    public int? TrialInterval { get; init; }

    /// <summary>Maxio <c>product.trial_interval_unit</c>, when the plan has a trial.</summary>
    public string? TrialIntervalUnit { get; init; }

    /// <summary>Maxio <c>product.trial_price_in_cents</c>, when the plan has a trial.</summary>
    public long? TrialPriceInCents { get; init; }

    /// <summary>Maxio <c>product.product_price_point_id</c> currently in effect for the product.</summary>
    public int? PricePointId { get; init; }

    /// <summary>Maxio <c>product.product_price_point_name</c>.</summary>
    public string? PricePointName { get; init; }

    /// <summary>Handle of the owning Maxio product family (Maxio <c>product.product_family.handle</c>).</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>Maxio <c>product.archived_at</c>. Archived plans are never offered for signup.</summary>
    public DateTimeOffset? ArchivedAt { get; init; }
}
