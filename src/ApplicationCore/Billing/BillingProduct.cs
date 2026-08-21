using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio product (plan) in the configured product family.
/// </summary>
public sealed class BillingProduct
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public bool RequireCreditCard { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }

    public decimal Price => PriceInCents / 100m;
    public bool IsArchived => ArchivedAt.HasValue;
}
