using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>
/// Application-facing view of a Maxio Advanced Billing subscription.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long ProductPriceInCents { get; set; }
    public int ProductInterval { get; set; }
    public string ProductIntervalUnit { get; set; } = string.Empty;
}
