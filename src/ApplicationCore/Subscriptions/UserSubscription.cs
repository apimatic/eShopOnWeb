using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription belonging to an eShopOnWeb user, as reported by the billing system.
/// </summary>
public class UserSubscription
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public long BalanceInCents { get; set; }
    public DateTimeOffset? NextBillingDateUtc { get; set; }
    public DateTimeOffset? NextAssessmentAtUtc { get; set; }
    public DateTimeOffset? ActivatedAtUtc { get; set; }
    public string? ProductFamilyHandle { get; set; }
}