namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription plan (a Maxio "product") offered within a product family.
/// This is a read-model describing the billing system of record; it is not persisted locally.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>List price expressed in the smallest currency unit (e.g. cents). Kept as an integer to avoid float drift.</summary>
    public int PriceInCents { get; init; }

    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;

    public bool Taxable { get; init; }

    /// <summary>Whether the plan requires a stored payment method at signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public string ProductFamilyHandle { get; init; } = string.Empty;

    public decimal Price => PriceInCents / 100m;

    public string BillingPeriod => IntervalUnit.ToLowerInvariant() switch
    {
        "day" => Interval == 1 ? "daily" : $"every {Interval} days",
        "week" => Interval == 1 ? "weekly" : $"every {Interval} weeks",
        "month" => Interval == 1 ? "monthly" : $"every {Interval} months",
        "year" => Interval == 1 ? "yearly" : $"every {Interval} years",
        _ => IntervalUnit
    };
}
