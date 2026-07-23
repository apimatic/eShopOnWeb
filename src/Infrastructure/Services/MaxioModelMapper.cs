using System;
using System.Globalization;
using System.Linq;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Translates Maxio SDK response models into the provider-agnostic domain types exposed by
/// <see cref="ApplicationCore.Interfaces.IBillingClient"/>. Keeping the translation here is what
/// stops any Maxio type from leaking past the single provider seam.
/// </summary>
/// <remarks>
/// Public so the UC0 operator tool can verify a seed through exactly the same translation the
/// running application uses, rather than a second, drifting interpretation of the same fields.
/// </remarks>
public static class MaxioModelMapper
{
    public static BillingPlan ToBillingPlan(Product product)
    {
        return new BillingPlan(
            id: product.Id ?? 0,
            handle: product.Handle ?? string.Empty,
            name: product.Name ?? string.Empty,
            description: product.Description,
            priceInCents: product.PriceInCents ?? 0L,
            interval: product.Interval ?? 0,
            intervalUnit: product.IntervalUnit?.Value ?? string.Empty,
            isArchived: product.ArchivedAt.HasValue);
    }

    public static BillingCustomer ToBillingCustomer(Customer customer)
    {
        return new BillingCustomer(
            id: customer.Id ?? 0,
            reference: customer.Reference,
            email: customer.Email,
            firstName: customer.FirstName,
            lastName: customer.LastName);
    }

    public static CustomerSubscription ToCustomerSubscription(Subscription subscription)
    {
        var providerState = subscription.State?.Value;

        return new CustomerSubscription(
            id: subscription.Id ?? 0,
            status: ToSubscriptionStatus(providerState),
            providerState: providerState,
            customerId: subscription.Customer?.Id,
            customerReference: subscription.Customer?.Reference,
            planHandle: subscription.Product?.Handle,
            planName: subscription.Product?.Name,
            planPriceInCents: subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents,
            currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
            nextAssessmentAt: subscription.NextAssessmentAt,
            activatedAt: subscription.ActivatedAt,
            canceledAt: subscription.CanceledAt,
            delayedCancelAt: subscription.DelayedCancelAt,
            cancelAtEndOfPeriod: subscription.CancelAtEndOfPeriod ?? false,
            nextPlanHandle: subscription.NextProductHandle);
    }

    public static ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent ToMeteredComponent(Component component)
    {
        return new ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent(
            id: component.Id ?? 0,
            handle: component.Handle,
            name: component.Name,
            kind: component.Kind?.Value,
            pricingScheme: component.PricingScheme?.Value,
            pricePerUnitInCents: ResolvePricePerUnitInCents(component),
            unitName: component.UnitName);
    }

    public static UsageRecord ToUsageRecord(Usage usage)
    {
        return new UsageRecord(
            id: usage.Id ?? 0L,
            quantity: ReadQuantity(usage.Quantity),
            memo: usage.Memo,
            componentId: usage.ComponentId,
            componentHandle: usage.ComponentHandle,
            subscriptionId: usage.SubscriptionId,
            createdAt: usage.CreatedAt);
    }

    public static ComponentUsageSummary ToUsageSummary(SubscriptionComponent component, long? pricePerUnitInCents)
    {
        return new ComponentUsageSummary(
            componentId: component.ComponentId,
            componentHandle: component.ComponentHandle,
            name: component.Name,
            unitBalance: component.UnitBalance ?? 0,
            pricePerUnitInCents: pricePerUnitInCents);
    }

    /// <summary>
    /// Maps the provider's subscription state onto the application's own state model. An unmodelled
    /// provider state maps to <see cref="SubscriptionStatus.Unknown"/> rather than throwing, so a new
    /// Maxio state never breaks a read path; the raw value is preserved alongside it.
    /// </summary>
    public static SubscriptionStatus ToSubscriptionStatus(string? providerState)
    {
        return providerState switch
        {
            "pending" => SubscriptionStatus.Pending,
            "trialing" => SubscriptionStatus.Trialing,
            "trial_ended" => SubscriptionStatus.TrialEnded,
            "assessing" => SubscriptionStatus.Assessing,
            "active" => SubscriptionStatus.Active,
            "soft_failure" => SubscriptionStatus.SoftFailure,
            "past_due" => SubscriptionStatus.PastDue,
            "suspended" => SubscriptionStatus.Suspended,
            "canceled" => SubscriptionStatus.Canceled,
            "expired" => SubscriptionStatus.Expired,
            "paused" => SubscriptionStatus.Paused,
            "on_hold" => SubscriptionStatus.OnHold,
            "unpaid" => SubscriptionStatus.Unpaid,
            "failed_to_create" => SubscriptionStatus.FailedToCreate,
            "awaiting_signup" => SubscriptionStatus.AwaitingSignup,
            _ => SubscriptionStatus.Unknown
        };
    }

    /// <summary>
    /// Reads a component's unit price in minor units. The provider may report the same figure in
    /// three places depending on how the component was created: an explicit cents field, a
    /// decimal-currency string, or the first price bracket. The explicit cents field is
    /// authoritative; the others are only consulted when it is absent.
    /// </summary>
    private static long? ResolvePricePerUnitInCents(Component component)
    {
        if (component.PricePerUnitInCents.HasValue)
        {
            return component.PricePerUnitInCents.Value;
        }

        var fromUnitPrice = ToCents(component.UnitPrice);
        if (fromUnitPrice.HasValue)
        {
            return fromUnitPrice;
        }

        // A per-unit component created with a price bracket carries its price on the first bracket
        // rather than on the flat unit_price field.
        var firstBracket = component.Prices?.FirstOrDefault(p => p is not null);
        return firstBracket is null ? null : ToCents(firstBracket.UnitPrice);
    }

    /// <summary>Parses a decimal-currency string into minor units, or <c>null</c> when it is unusable.</summary>
    private static long? ToCents(string? amount)
    {
        if (string.IsNullOrWhiteSpace(amount) ||
            !decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return null;
        }

        return (long)Math.Round(parsed * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Reads the accepted usage quantity, which the provider may report either as a number or as a
    /// string.
    /// </summary>
    private static decimal ReadQuantity(MaxioAdvancedBilling.Models.AnyOf.Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0m;
        }

        if (quantity.TryGetInt(out var intValue))
        {
            return intValue;
        }

        if (quantity.TryGetString(out var stringValue) &&
            decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }
}
