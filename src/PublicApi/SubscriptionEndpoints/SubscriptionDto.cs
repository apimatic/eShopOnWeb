using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>The recurring price in minor units, exactly as the provider holds it.</summary>
    public long PlanPriceInCents { get; set; }

    /// <summary>The recurring price in major units (dollars).</summary>
    public decimal PlanPrice { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public string? NextPlanHandle { get; set; }
    public bool IsLive { get; set; }

    /// <summary>The lifecycle actions currently legal for this subscription.</summary>
    public List<string> LegalActions { get; set; } = new();
}
