using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.SubscriptionBilling;

/// <summary>
/// A user's subscription as reflected by the billing system of record.
/// </summary>
public class UserSubscription
{
    /// <summary>
    /// Subscription id in the billing system. Not stable across re-seeds; treat as opaque.
    /// </summary>
    public int Id { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring amount for the currently subscribed product version.
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Subscription state in the billing system (e.g., active, canceled, past_due).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// When the next regularly scheduled billing will occur, if applicable.
    /// </summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// The eShopOnWeb user id that the billing customer was created with.
    /// </summary>
    public string? CustomerReference { get; set; }
}