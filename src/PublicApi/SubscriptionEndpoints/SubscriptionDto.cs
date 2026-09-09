using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the current user, mapped from a Maxio subscription.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Maxio subscription state (active, trialing, past_due, canceled, ...).</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price per billing interval, in currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price per billing interval, in integer cents.</summary>
    public long PriceInCents { get; set; }

    public int BillingInterval { get; set; }

    public string BillingIntervalUnit { get; set; } = string.Empty;

    /// <summary>When the next billing attempt is scheduled (next assessment, falling back to period end).</summary>
    public DateTime? NextBillingAt { get; set; }

    public DateTime? CurrentPeriodEndsAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>True when the call replayed an existing subscription instead of creating a new one.</summary>
    public bool AlreadySubscribed { get; set; }
}
