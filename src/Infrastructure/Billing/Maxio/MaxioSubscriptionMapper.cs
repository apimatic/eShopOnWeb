using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Subscriptions.Subscription;
using WireSubscription = Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Wire.MaxioSubscription;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Translates Maxio wire shapes into the provider-neutral models in ApplicationCore.
/// </summary>
internal static class MaxioSubscriptionMapper
{
    public static SubscriptionPlan ToPlan(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle ?? product.Id.ToString(CultureInfo.InvariantCulture),
        Name = product.Name ?? product.Handle ?? "Unnamed plan",
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        Price = FromCents(product.PriceInCents),
        Currency = currency,
        Interval = new BillingInterval(product.Interval, ToIntervalUnit(product.IntervalUnit)),
        RequiresPaymentMethod = product.RequireCreditCard,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
        TrialDescription = DescribeTrial(product)
    };

    public static DomainSubscription ToSubscription(
        WireSubscription subscription,
        string currency,
        string customerReference,
        SubscriptionPlan? plan)
    {
        var planHandle = subscription.Product?.Handle ?? plan?.Handle ?? string.Empty;
        var price = subscription.ProductPriceInCents is { } cents
            ? FromCents(cents)
            : plan?.Price ?? FromCents(subscription.Product?.PriceInCents ?? 0);

        var interval = subscription.Product is { } product
            ? new BillingInterval(product.Interval, ToIntervalUnit(product.IntervalUnit))
            : plan?.Interval ?? BillingInterval.Unknown;

        var state = ToState(subscription.State);

        return new DomainSubscription
        {
            Id = subscription.Id.ToString(CultureInfo.InvariantCulture),
            State = state,
            ProviderState = subscription.State ?? "unknown",
            PlanHandle = planHandle,
            PlanName = subscription.Product?.Name ?? plan?.Name ?? planHandle,
            Price = price,
            Currency = currency,
            Interval = interval,
            // next_assessment_at is the authoritative "when will we charge next", because it
            // diverges from the period end while a failed payment is being retried.
            NextBillingAt = state == SubscriptionState.Ended
                ? null
                : subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = (subscription.Customer?.Id ?? 0).ToString(CultureInfo.InvariantCulture),
            CustomerReference = subscription.Customer?.Reference ?? customerReference,
            Reference = subscription.Reference
        };
    }

    /// <summary>
    /// Buckets Maxio's subscription states. The groupings follow Maxio's own "live / problem /
    /// end of life" taxonomy.
    /// </summary>
    public static SubscriptionState ToState(string? providerState) =>
        (providerState ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "active" or "trialing" => SubscriptionState.Active,

            "pending" or "assessing" or "awaiting_signup" => SubscriptionState.Pending,

            "past_due" or "soft_failure" or "unpaid" or "paused" => SubscriptionState.ProblemState,

            "canceled" or "cancelled" or "expired" or "failed_to_create"
                or "on_hold" or "suspended" or "trial_ended" => SubscriptionState.Ended,

            _ => SubscriptionState.Unknown
        };

    /// <summary>
    /// True when a subscription still occupies the shopper's slot on a plan, so re-subscribing
    /// would double-bill. Unrecognised states count as occupied: refusing a duplicate signup is
    /// the safer failure.
    /// </summary>
    public static bool OccupiesPlan(SubscriptionState state) => state != SubscriptionState.Ended;

    public static BillingIntervalUnit ToIntervalUnit(string? unit) =>
        (unit ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "day" => BillingIntervalUnit.Day,
            "month" => BillingIntervalUnit.Month,
            _ => BillingIntervalUnit.Unknown
        };

    private static decimal FromCents(long cents) => cents / 100m;

    private static string? DescribeTrial(MaxioProduct product)
    {
        if (product.TrialInterval is not > 0)
        {
            return null;
        }

        var unit = ToIntervalUnit(product.TrialIntervalUnit);
        var unitLabel = unit == BillingIntervalUnit.Unknown ? "period" : unit.ToString().ToLowerInvariant();
        var plural = product.TrialInterval == 1 ? string.Empty : "s";
        var price = FromCents(product.TrialPriceInCents ?? 0);

        return price == 0m
            ? $"{product.TrialInterval} {unitLabel}{plural} free"
            : $"{product.TrialInterval} {unitLabel}{plural} at {price.ToString("0.00", CultureInfo.InvariantCulture)}";
    }
}
