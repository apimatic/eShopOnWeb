using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A subscription as represented in Maxio Advanced Billing.
/// </summary>
public class MaxioSubscription
{
    public int Id { get; init; }
    public string State { get; init; } = string.Empty;
    public long? ProductPriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? Reference { get; init; }
    public MaxioCustomer? Customer { get; init; }
    public MaxioProduct? Product { get; init; }
}
