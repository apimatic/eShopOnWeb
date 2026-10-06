using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription owned by an eShopOnWeb user, backed by Maxio Advanced Billing.
/// </summary>
public record Subscription(
    int Id,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    int? BalanceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt,
    string? Reference)
{
    public decimal Price => PriceInCents / 100m;
}
