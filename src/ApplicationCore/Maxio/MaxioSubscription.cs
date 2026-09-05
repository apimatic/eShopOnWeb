using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio subscription, as reported by Maxio (the billing system of record).
/// </summary>
public class MaxioSubscription
{
    public MaxioSubscription(
        long id,
        string state,
        string? planHandle,
        string? planName,
        int? priceInCents,
        DateTimeOffset? currentPeriodEndsAt,
        DateTimeOffset? nextAssessmentAt,
        DateTimeOffset? activatedAt)
    {
        Id = id;
        State = state;
        PlanHandle = planHandle;
        PlanName = planName;
        PriceInCents = priceInCents;
        CurrentPeriodEndsAt = currentPeriodEndsAt;
        NextAssessmentAt = nextAssessmentAt;
        ActivatedAt = activatedAt;
    }

    public long Id { get; }
    public string State { get; }
    public string? PlanHandle { get; }
    public string? PlanName { get; }
    public int? PriceInCents { get; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; }
    public DateTimeOffset? NextAssessmentAt { get; }
    public DateTimeOffset? ActivatedAt { get; }
}
