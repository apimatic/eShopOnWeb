namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Sourced from the billing system of record;
/// eShopOnWeb never stores plan definitions of its own.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan in the billing system (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Identifier assigned by the billing system. Not stable across catalog re-seeds - prefer <see cref="Handle"/>.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code the plan is billed in (site currency).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with <c>month</c> = monthly).</summary>
    public int Interval { get; set; }

    /// <summary><c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when the billing system requires a stored payment method before the plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Trial length, expressed in <see cref="TrialIntervalUnit"/>s. Null when the plan has no trial.</summary>
    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Trial price in cents, when the plan has a (paid) trial.</summary>
    public long? TrialPriceInCents { get; set; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Name of the price point the plan is quoted at.</summary>
    public string? PricePointName { get; set; }
}
