using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A buyer's subscription as it currently stands in the billing system of record (Maxio Advanced Billing).
/// </summary>
public class Subscription
{
    public int MaxioSubscriptionId { get; init; }
    public string PlanHandle { get; init; } = default!;
    public string PlanName { get; init; } = default!;
    public string State { get; init; } = default!;
    public long PriceInCents { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
}
