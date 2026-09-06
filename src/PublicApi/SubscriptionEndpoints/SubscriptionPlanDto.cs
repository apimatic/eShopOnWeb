using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan a shopper can enrol on.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable handle of the plan. Pass this as <c>planHandle</c> when subscribing.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in minor currency units (e.g. cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. <c>month</c>.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable price, e.g. <c>299.00 USD / month</c>.</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    /// <summary>True when a payment method must be on file before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>True when the plan starts with a trial period.</summary>
    public bool HasTrial { get; set; }

    /// <summary>Product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }

    public static SubscriptionPlanDto FromPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = SubscriptionMoney.ToDecimal(plan.PriceInCents),
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        DisplayPrice = SubscriptionMoney.FormatRecurring(plan.PriceInCents, plan.Currency, plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.TrialIntervalLength is > 0,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };
}
