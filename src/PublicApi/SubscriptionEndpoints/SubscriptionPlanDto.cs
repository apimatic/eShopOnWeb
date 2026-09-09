namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan, mapped from a Maxio product.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing interval, in currency units.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price per billing interval, in integer cents.</summary>
    public long PriceInCents { get; set; }

    public int BillingInterval { get; set; }

    public string BillingIntervalUnit { get; set; } = string.Empty;

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when a payment method must be captured at signup.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }
}
