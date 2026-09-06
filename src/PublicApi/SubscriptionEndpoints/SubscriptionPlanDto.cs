namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable API handle of the plan. Pass this back to POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor currency units, exactly as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Convenience rendering of <see cref="PriceInCents"/> in major units.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO currency code of the billing site; empty when the site does not report one.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How often the plan recurs, e.g. <c>1</c> / <c>month</c>.</summary>
    public BillingIntervalDto Interval { get; set; } = new();

    /// <summary>Human readable summary, e.g. <c>USD 299.00 every month</c>.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>One-time charge applied at signup, when the plan defines one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>Trial length, when the plan offers one.</summary>
    public BillingIntervalDto? Trial { get; set; }

    public long? TrialPriceInCents { get; set; }

    /// <summary>True when the plan cannot be subscribed to without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// The provider's numeric product id, for support and traceability. Handles are stable across a
    /// catalogue reseed; ids are not, so do not store this value.
    /// </summary>
    public int ProductId { get; set; }
}

/// <summary>A recurrence expressed as a length and a unit (<c>day</c> or <c>month</c>).</summary>
public class BillingIntervalDto
{
    public int Length { get; set; }

    public string Unit { get; set; } = string.Empty;
}
