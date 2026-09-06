namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Value to send as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals.</summary>
    public int Interval { get; set; }

    /// <summary>Renewal interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable price, e.g. "USD 299.00 / month".</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before subscribing.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool Taxable { get; set; }

    /// <summary>Length of the free/discounted trial, or null when the plan has no trial.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>One-off signup charge in the smallest currency unit, when the plan has one.</summary>
    public long? SetupFeeInCents { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
