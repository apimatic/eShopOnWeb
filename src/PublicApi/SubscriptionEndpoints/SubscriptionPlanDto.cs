namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// Stable identifier of the plan, and the value to post to /api/subscriptions.
    /// Prefer this over <see cref="Id"/>: handles are stable, numeric ids are reassigned when the
    /// billing catalog is re-seeded.
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Numeric id in the billing system. Informational; not stable across re-seeds.</summary>
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, for exact arithmetic.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO currency code, e.g. "USD". Null if the billing site did not report one.</summary>
    public string? Currency { get; set; }

    /// <summary>Length of one billing period, counted in <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the plan needs a stored payment method, which this API does not capture.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
