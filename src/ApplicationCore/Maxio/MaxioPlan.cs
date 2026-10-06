using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscription plan (Maxio "product") available in the configured product family.
/// </summary>
public record MaxioPlan(
    long ProductId,
    string Handle,
    string Name,
    string? Description,
    int PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod,
    string FamilyHandle,
    string FamilyName,
    DateTime? ArchivedAt)
{
    public bool IsArchived => ArchivedAt.HasValue;
}
