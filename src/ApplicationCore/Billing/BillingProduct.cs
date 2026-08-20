using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingProduct
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
}
