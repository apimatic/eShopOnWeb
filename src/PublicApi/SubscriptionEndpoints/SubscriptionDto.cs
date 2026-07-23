using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as eShopOnWeb exposes it.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>The eShopOnWeb user the subscription belongs to.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>The normalized lifecycle state, for example <c>Active</c>.</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in dollars.</summary>
    public decimal PlanPrice { get; set; }

    /// <summary>Recurring price in cents, as the billing provider reports it.</summary>
    public long PlanPriceInCents { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? ScheduledCancellationAt { get; set; }

    /// <summary>A plan change already scheduled for the next renewal, if any.</summary>
    public string? ScheduledPlanHandle { get; set; }

    /// <summary>The lifecycle actions that are legal from the current state.</summary>
    public List<string> AllowedActions { get; set; } = new();
}
