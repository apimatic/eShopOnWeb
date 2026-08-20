using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A Maxio subscription for a shopper. Maxio is the system of record.
/// </summary>
public sealed class BillingSubscription
{
    public BillingSubscription(
        int id,
        string state,
        string? productHandle,
        string? productName,
        int productPriceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        string? reference)
    {
        Id = id;
        State = state;
        ProductHandle = productHandle;
        ProductName = productName;
        ProductPriceInCents = productPriceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        Reference = reference;
    }

    public int Id { get; }
    public string State { get; }
    public string? ProductHandle { get; }
    public string? ProductName { get; }
    public int ProductPriceInCents { get; }
    public decimal Price => ProductPriceInCents / 100m;
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    /// <summary>
    /// Next billing date: the end of the current period, which is when the next
    /// regularly scheduled charge occurs. Falls back to next assessment if needed.
    /// </summary>
    public DateTimeOffset? NextBillingAt => CurrentPeriodEndsAt ?? NextAssessmentAt;
    public string? Reference { get; }
}
