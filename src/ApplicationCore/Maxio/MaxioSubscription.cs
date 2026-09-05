using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>An eShopOnWeb user's subscription as recorded in Maxio Advanced Billing.</summary>
public class MaxioSubscription
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>
    /// True when this call created the subscription; false when an existing, still-live
    /// subscription to the same plan was returned instead (idempotent subscribe).
    /// </summary>
    public bool IsNewlyCreated { get; set; }
}
