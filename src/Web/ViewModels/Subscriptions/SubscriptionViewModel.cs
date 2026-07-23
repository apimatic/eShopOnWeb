using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Web.ViewModels.Subscriptions;

/// <summary>One of the customer's subscriptions as shown on the storefront management page.</summary>
public class SubscriptionViewModel
{
    public long Id { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public SubscriptionState State { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public bool CancelAtEndOfPeriod { get; set; }

    public DateTimeOffset? DelayedCancelAt { get; set; }

    /// <summary>Set when a plan change is already scheduled for the next renewal.</summary>
    public string? NextPlanHandle { get; set; }

    /// <summary>The plan this subscription can be switched to, or null when no alternative exists.</summary>
    public string? AlternatePlanHandle { get; set; }

    public string? AlternatePlanName { get; set; }

    /// <summary>Metered units accrued in the current period; null when the total is unavailable.</summary>
    public int? PeriodToDateUnits { get; set; }

    public decimal? UsageUnitPrice { get; set; }

    public decimal? EstimatedUsageCharge { get; set; }

    public string UsageComponentHandle { get; set; } = string.Empty;
}
