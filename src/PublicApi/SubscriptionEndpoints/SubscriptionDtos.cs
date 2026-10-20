using System;
using System.Globalization;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? PriceInCents { get; set; }
    public string? FormattedPrice { get; set; }

    /// <summary>The plan list does not carry a currency; it is reported on each subscription.</summary>
    public string? Currency { get; set; }

    public int? Interval { get; set; }

    /// <summary>Provider wire value, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    public static SubscriptionPlanDto From(BillingPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = PriceFormatter.Format(plan.PriceInCents, null),
        Currency = null,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? FormattedPrice { get; set; }
    public string? Currency { get; set; }

    /// <summary>Provider wire value, e.g. "active", "trialing", "past_due".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Next assessment date, or the end of the current period when the provider gives none.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>Provider wire value, e.g. "remittance" (invoice-billed) or "automatic".</summary>
    public string? PaymentCollectionMethod { get; set; }

    public static SubscriptionDto From(BillingSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        Reference = subscription.Reference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = PriceFormatter.Format(subscription.PriceInCents, subscription.Currency),
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
    };
}

public static class PriceFormatter
{
    /// <summary>"$299.00" for USD, "299.00 EUR" for other currencies, "299.00" when the currency is unknown.</summary>
    public static string? Format(long? cents, string? currency)
    {
        if (cents is null)
        {
            return null;
        }
        var amount = (cents.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(currency))
        {
            return amount;
        }
        return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? "$" + amount
            : amount + " " + currency.ToUpperInvariant();
    }
}
