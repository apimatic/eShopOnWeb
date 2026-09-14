namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan a shopper can enrol in.</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier. Pass this as <c>planHandle</c> to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The product family the plan belongs to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Recurring price in the smallest currency unit, e.g. 29900 for $299.00.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO currency code, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when the plan cannot start without a stored payment method.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Price point the plan is quoted at.</summary>
    public string? PricePointHandle { get; set; }
}
