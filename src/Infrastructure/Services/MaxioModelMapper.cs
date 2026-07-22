using System;
using System.Globalization;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.AnyOf;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using DomainMeteredComponent = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.MeteredComponent;
using DomainSubscription = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.Subscription;
using DomainSubscriptionState = Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate.SubscriptionState;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Translates Maxio's wire models into eShopOnWeb's provider-agnostic domain types.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place Maxio's shapes are understood. Two conventions matter and are enforced
/// here rather than at every call site:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Units.</b> Maxio reports most money in integer cents (<c>*_in_cents</c>) but component unit
/// prices as a decimal string in dollars. The domain speaks only dollars, so every conversion
/// happens on the way through.
/// </item>
/// <item>
/// <b>Unknown values.</b> Maxio's enums are open — a state this integration has never seen
/// deserializes successfully rather than throwing. Those map to
/// <see cref="DomainSubscriptionState.Unknown"/> while the provider's own name is preserved, so an
/// unrecognized state is visible instead of silently looking active.
/// </item>
/// </list>
/// </remarks>
internal static class MaxioModelMapper
{
    private const decimal CentsPerUnit = 100m;

    /// <summary>Converts Maxio's integer minor units to whole currency units.</summary>
    internal static decimal CentsToDollars(long? cents) => (cents ?? 0L) / CentsPerUnit;

    /// <summary>
    /// Maps a product to a plan, or returns null when the product cannot be offered — it has no
    /// stable handle, so nothing could ever subscribe to it reliably.
    /// </summary>
    internal static BillingPlan? TryMapPlan(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.Handle))
        {
            return null;
        }

        return new BillingPlan(
            id: product.Id ?? 0,
            handle: product.Handle,
            name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle : product.Name,
            price: CentsToDollars(product.PriceInCents),
            intervalLength: product.Interval ?? 1,
            intervalUnit: product.IntervalUnit?.Value ?? "month")
        {
            Description = product.Description,
            // Maxio carries two similar flags: request_credit_card only asks the hosted signup page
            // to show a card field, while require_credit_card is the one that actually refuses an
            // enrolment without a payment profile. Only the latter is a precondition here.
            RequiresPaymentMethod = product.RequireCreditCard ?? false,
            IsArchived = product.ArchivedAt.HasValue,
            ProductFamilyHandle = product.ProductFamily?.Handle
        };
    }

    /// <summary>Maps a catalog component, preserving Maxio's kind verbatim so a mismatch reads clearly.</summary>
    internal static DomainMeteredComponent MapComponent(Component component)
    {
        var handle = component.Handle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            throw new BillingProviderException(
                $"Maxio returned component {component.Id} without a handle, so it cannot be addressed.");
        }

        return new DomainMeteredComponent(
            id: component.Id ?? 0,
            handle: handle,
            name: string.IsNullOrWhiteSpace(component.Name) ? handle : component.Name,
            kind: component.Kind?.Value ?? "unknown",
            isMetered: component.Kind is not null && component.Kind == ComponentKind.MeteredComponent,
            unitPrice: ReadUnitPrice(component))
        {
            IsArchived = component.ArchivedAt.HasValue || (component.Archived ?? false)
        };
    }

    /// <summary>
    /// Maps a customer, or returns null when it carries no id — without one it cannot be used to
    /// create a subscription.
    /// </summary>
    internal static BillingCustomer? TryMapCustomer(Customer? customer)
    {
        if (customer?.Id is not int id || id <= 0)
        {
            return null;
        }

        var reference = ResolveCustomerReference(customer);
        var email = string.IsNullOrWhiteSpace(customer.Email) ? reference : customer.Email;

        return new BillingCustomer(id, reference, email)
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName
        };
    }

    /// <summary>Maps a subscription together with its nested plan and customer.</summary>
    /// <exception cref="BillingProviderException">
    /// The response is missing an id or the nested product, so it cannot be represented faithfully.
    /// </exception>
    internal static DomainSubscription MapSubscription(MaxioAdvancedBilling.Models.Subscription? subscription)
    {
        if (subscription?.Id is not int id || id <= 0)
        {
            throw new BillingProviderException("Maxio returned a subscription without an id.");
        }

        var plan = TryMapPlan(subscription.Product)
                   ?? throw new BillingProviderException(
                       $"Maxio returned subscription {id} without usable product details.");

        var providerState = subscription.State?.Value ?? "unknown";

        return new DomainSubscription(
            id: id,
            userReference: ResolveCustomerReference(subscription.Customer),
            customerId: subscription.Customer?.Id ?? 0,
            plan: plan,
            state: MapState(subscription.State),
            providerState: providerState)
        {
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            DelayedCancelAt = subscription.DelayedCancelAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
            Balance = CentsToDollars(subscription.BalanceInCents),
            PendingPlanHandle = subscription.NextProductHandle
        };
    }

    /// <summary>Maps a recorded usage, reading the quantity through both branches of its union.</summary>
    internal static UsageRecord MapUsage(Usage usage)
    {
        return new UsageRecord(
            id: usage.Id ?? 0L,
            quantity: ReadQuantity(usage.Quantity),
            memo: usage.Memo,
            recordedAt: usage.CreatedAt);
    }

    /// <summary>
    /// Normalizes Maxio's subscription state. The hold endpoint produces <c>on_hold</c>; Maxio's
    /// own <c>paused</c> is an internal arrears state, and both mean "not billing right now" here.
    /// </summary>
    internal static DomainSubscriptionState MapState(MaxioAdvancedBilling.Models.Enums.SubscriptionState? state)
    {
        return state?.Value switch
        {
            "pending" or "awaiting_signup" or "failed_to_create" => DomainSubscriptionState.Pending,
            "trialing" => DomainSubscriptionState.Trialing,
            "active" or "assessing" => DomainSubscriptionState.Active,
            "past_due" or "soft_failure" or "unpaid" or "suspended" => DomainSubscriptionState.PastDue,
            "on_hold" or "paused" => DomainSubscriptionState.Paused,
            "canceled" => DomainSubscriptionState.Canceled,
            "expired" or "trial_ended" => DomainSubscriptionState.Expired,
            _ => DomainSubscriptionState.Unknown
        };
    }

    /// <summary>
    /// Reads Maxio's <c>int | string</c> usage quantity. A quantity that cannot be read as a whole
    /// number would silently under-count the bill, so it is rejected rather than defaulted.
    /// </summary>
    internal static int ReadQuantity(Quantity1? quantity)
    {
        if (quantity is null)
        {
            return 0;
        }

        if (quantity.TryGetInt(out var asInt))
        {
            return asInt;
        }

        if (quantity.TryGetString(out var asString) &&
            decimal.TryParse(asString, NumberStyles.Number, CultureInfo.InvariantCulture, out var asDecimal))
        {
            return (int)decimal.Truncate(asDecimal);
        }

        throw new BillingProviderException(
            "Maxio returned a usage quantity that could not be read as a number.");
    }

    /// <summary>
    /// The eShopOnWeb user a Maxio customer belongs to.
    /// </summary>
    /// <remarks>
    /// Only the reference this integration itself wrote counts. A customer created out-of-band in
    /// the Maxio UI has no such reference, and it deliberately falls back to a synthetic value that
    /// cannot equal any eShopOnWeb username — including via the email address, which would
    /// otherwise let an out-of-band record attach itself to a real account. Such a customer stays
    /// invisible to customer-facing flows and reachable only by an administrator.
    /// </remarks>
    private static string ResolveCustomerReference(Customer? customer)
    {
        if (!string.IsNullOrWhiteSpace(customer?.Reference))
        {
            return customer.Reference;
        }

        return $"maxio-customer-{customer?.Id?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    }

    /// <summary>
    /// Maxio reports a component's unit price as a decimal string in dollars, and separately in
    /// integer cents. Prefer the dollar string, fall back to the cents field.
    /// </summary>
    private static decimal ReadUnitPrice(Component component)
    {
        if (!string.IsNullOrWhiteSpace(component.UnitPrice) &&
            decimal.TryParse(component.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) &&
            price >= 0m)
        {
            return price;
        }

        return CentsToDollars(component.PricePerUnitInCents);
    }
}
