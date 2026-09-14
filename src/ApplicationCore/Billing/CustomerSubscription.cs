using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb shopper.
/// </summary>
public class CustomerSubscription
{
    public required int MaxioSubscriptionId { get; init; }
    public required string PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long? PriceInCents { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
