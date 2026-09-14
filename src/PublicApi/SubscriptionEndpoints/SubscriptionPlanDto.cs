using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable recurring plan. <see cref="Handle"/> is the stable identifier — provider numeric ids
/// change whenever the catalog is re-seeded, so none is exposed.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit, for exact arithmetic.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units, for display.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Ready-to-display price, e.g. <c>299.00 USD</c>.</summary>
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>Ready-to-display billing cadence, e.g. <c>every month</c>.</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    public bool HasTrial { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }

    public long SetupFeeInCents { get; set; }

    /// <summary>True when subscribing to this plan needs payment details, which this API does not collect.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto FromPlan(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = SubscriptionFormatting.ToMajorUnits(plan.PriceInCents),
        Currency = plan.Currency,
        FormattedPrice = SubscriptionFormatting.FormatMoney(plan.PriceInCents, plan.Currency),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = SubscriptionFormatting.DescribeBillingPeriod(plan.Interval, plan.IntervalUnit),
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        TrialPriceInCents = plan.TrialPriceInCents,
        SetupFeeInCents = plan.SetupFeeInCents,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };
}
