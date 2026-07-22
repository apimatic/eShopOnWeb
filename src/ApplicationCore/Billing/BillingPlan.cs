namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A recurring plan a customer can subscribe to, expressed in provider-agnostic terms.
/// </summary>
public class BillingPlan
{
    /// <summary>
    /// Provider-assigned identifier. Not stable across a re-seed of the catalog; prefer <see cref="Handle"/>.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Stable, human-authored identifier for the plan (e.g. <c>eshop-pro</c>).
    /// </summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Recurring price expressed in major currency units (e.g. 299.00 for $299.00).
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).
    /// </summary>
    public int Interval { get; set; }

    /// <summary>
    /// Unit the billing period is measured in (e.g. <c>month</c>).
    /// </summary>
    public string? IntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// True when the provider requires a payment method before the plan can be subscribed to.
    /// </summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>
    /// True when the plan has been archived at the provider and can no longer be subscribed to.
    /// </summary>
    public bool IsArchived { get; set; }
}
