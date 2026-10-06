using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

public class MaxioSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public long BalanceInCents { get; set; }
    public long ProductPriceInCents { get; set; }
    public int ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public int ProductInterval { get; set; }
    public string ProductIntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? Reference { get; set; }
    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }
}
