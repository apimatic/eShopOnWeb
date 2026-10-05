using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A user's subscription as recorded by the billing system (Maxio Advanced Billing).
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The Maxio subscription id
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Maxio subscription state (active, trialing, past_due, canceled, ...)
    /// </summary>
    public string State { get; set; } = string.Empty;

    public int ProductId { get; set; }

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Recurring price per billing period
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// The Maxio customer id this subscription belongs to
    /// </summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// When the next regularly scheduled charge will occur (next billing date)
    /// </summary>
    public DateTime? NextBillingAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? CanceledAt { get; set; }

    /// <summary>
    /// Whether the subscription is scheduled to cancel at the end of the current period
    /// </summary>
    public bool CancelAtEndOfPeriod { get; set; }
}