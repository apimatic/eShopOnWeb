namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A subscription plan available for purchase, sourced from the billing system (Maxio Advanced Billing).
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>
    /// The Maxio product id (not stable across site re-seeds; the handle is the stable identifier)
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// The Maxio product handle (stable identifier of the plan)
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price per billing period
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Billing interval length (e.g. 1)
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Billing interval unit (day or month)
    /// </summary>
    public string IntervalUnit { get; set; } = "month";

    /// <summary>
    /// Handle of the product family this plan belongs to
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public bool Archived { get; set; }
}