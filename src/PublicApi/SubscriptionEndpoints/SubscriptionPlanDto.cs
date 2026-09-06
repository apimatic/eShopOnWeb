namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan. Send this back in POST /api/subscriptions.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>Recurring price in minor units, e.g. 29900. Authoritative; <see cref="Price"/> is derived.</summary>
    public long PriceInCents { get; set; }

    /// <summary>ISO 4217 currency code, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Ready-to-display price, e.g. "299.00 USD / month".</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    /// <summary>How many <see cref="BillingIntervalUnit"/>s each billing period spans.</summary>
    public int BillingIntervalLength { get; set; }

    /// <summary>Billing period unit as named by the billing provider, e.g. "month".</summary>
    public string BillingIntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the billing provider requires a payment method before this plan can be started.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }

    public int? TrialIntervalLength { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Handle of the product family (catalog) this plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
