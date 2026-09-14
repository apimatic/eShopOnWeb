using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription enrollment as recorded by the billing provider (Maxio Advanced Billing).
/// </summary>
public class CustomerSubscription
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }

    /// <summary>
    /// Timestamp of the next attempted payment capture for this subscription (Maxio's @next_assessment_at@).
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
