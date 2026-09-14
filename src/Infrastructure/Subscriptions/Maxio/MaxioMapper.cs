using System.Globalization;
using AdvancedBilling.Standard.Models;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Projects Maxio SDK models onto the application's own subscription model. Provider vocabulary
/// (state names, interval units, collection methods) is passed through verbatim rather than remapped,
/// so what eShopOnWeb reports always matches what an operator sees in Maxio.
/// </summary>
internal static class MaxioMapper
{
    private const string UnknownState = "unknown";

    public static SubscriptionPlan ToPlan(Product product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents ?? 0,
        Currency = currency,
        Interval = ToInterval(product.Interval, product.IntervalUnit),
        ProductFamilyHandle = product.ProductFamily?.Handle,
        RequiresPaymentMethod = product.RequireCreditCard ?? false,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = MaxioEnum.ToWireName(product.TrialIntervalUnit),
    };

    public static CustomerSubscription ToSubscription(Subscription subscription, string currency) => new()
    {
        Id = subscription.Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        State = MaxioEnum.ToWireName(subscription.State) ?? UnknownState,
        Reference = subscription.Reference,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0,
        Currency = currency,
        Interval = subscription.Product is null
            ? null
            : ToInterval(subscription.Product.Interval, subscription.Product.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextAssessmentAt,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        BalanceInCents = subscription.BalanceInCents ?? 0,
        PaymentCollectionMethod = MaxioEnum.ToWireName(subscription.PaymentCollectionMethod),
        CustomerId = subscription.Customer?.Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static BillingInterval ToInterval(int? length, IntervalUnit? unit) =>
        new(length ?? 1, MaxioEnum.ToWireName(unit) ?? BillingInterval.Monthly.Unit);
}
