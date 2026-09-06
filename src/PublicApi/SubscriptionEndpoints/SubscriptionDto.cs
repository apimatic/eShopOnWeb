using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a plan, as the billing provider currently reports it.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Provider state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while this subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }

    /// <summary>Ready-to-display price, e.g. <c>299.00 USD</c>.</summary>
    public string? FormattedPrice { get; set; }

    public int? CustomerId { get; set; }

    /// <summary>The provider-side key this eShopOnWeb account maps to.</summary>
    public string? CustomerReference { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>How the provider collects this subscription, e.g. <c>remittance</c> or <c>automatic</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>
    /// False when the request found an existing subscription instead of enrolling. A repeated subscribe
    /// is answered rather than rejected, and this is how a client tells the two apart.
    /// </summary>
    public bool WasCreatedByThisRequest { get; set; }

    public static SubscriptionDto FromSubscription(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsActive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents is null
            ? null
            : SubscriptionFormatting.ToMajorUnits(subscription.PriceInCents.Value),
        Currency = subscription.Currency,
        FormattedPrice = subscription.PriceInCents is null
            ? null
            : SubscriptionFormatting.FormatMoney(subscription.PriceInCents.Value, subscription.Currency),
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        TrialEndsAt = subscription.TrialEndsAt,
        CanceledAt = subscription.CanceledAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        WasCreatedByThisRequest = subscription.WasCreatedByThisRequest
    };
}
