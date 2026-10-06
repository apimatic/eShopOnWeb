using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The caller's subscription as recorded by the billing system.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>
    /// Subscription state in the billing system (e.g., active, canceled, past_due).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Next scheduled billing date, when applicable.
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }
}