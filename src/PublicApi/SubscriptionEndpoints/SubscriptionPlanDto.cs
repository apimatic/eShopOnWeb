using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to. <see cref="Handle"/> is the stable identifier to send back
/// when subscribing - the billing system reassigns numeric ids when the catalog is re-seeded.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in cents, e.g. 29900.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>ISO 4217 currency code, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>Renewal cadence, e.g. 1 with an <see cref="IntervalUnit"/> of "month".</summary>
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method must be captured before the shopper can subscribe.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public bool HasTrial { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public static SubscriptionPlanDto FromPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };
}
