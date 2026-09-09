using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A subscription in Maxio Advanced Billing (the API's subscription object).
/// Only the fields this integration consumes are mapped.
/// </summary>
public class MaxioSubscription
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public long ProductPriceInCents { get; set; }
    public long BalanceInCents { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public DateTime? DelayedCancelAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}
