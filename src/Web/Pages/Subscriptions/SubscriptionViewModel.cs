using System;

namespace Microsoft.eShopWeb.Web.Pages.Subscriptions;

public class SubscriptionViewModel
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The handle of the other configured plan — the only plan-change target in this demo.</summary>
    public string OtherPlanHandle { get; set; } = string.Empty;

    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);
    public bool IsPaused => string.Equals(State, "paused", StringComparison.OrdinalIgnoreCase)
        || string.Equals(State, "on_hold", StringComparison.OrdinalIgnoreCase);
    public bool IsCanceled => string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase)
        || string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase);
}
