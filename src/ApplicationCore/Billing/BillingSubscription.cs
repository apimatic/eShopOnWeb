using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class BillingSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public string? Reference { get; init; }
    public long ProductPriceInCents { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public BillingProduct? Product { get; init; }
}
