using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting pay-as-you-go usage against a subscription (UC2).
/// </summary>
public class UsageReceipt
{
    public int Id { get; init; }
    public int SubscriptionId { get; init; }
    public int ComponentId { get; init; }
    public string? ComponentHandle { get; init; }
    public decimal Quantity { get; init; }
    public string? Memo { get; init; }
    public DateTimeOffset? RecordedAt { get; init; }

    /// <summary>
    /// The running period-to-date unit total, or <see langword="null"/> when the read-back
    /// failed after the usage was successfully recorded (the usage still stands — plan UC2).
    /// </summary>
    public decimal? PeriodToDateTotal { get; init; }
}
