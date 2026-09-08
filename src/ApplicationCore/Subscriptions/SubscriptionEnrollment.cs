using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Currency { get; set; }
    public long BalanceInCents { get; set; }
    public long PriceInCents { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}
