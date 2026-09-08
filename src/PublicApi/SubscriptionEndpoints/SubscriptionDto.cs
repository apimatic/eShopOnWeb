using System;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as recorded in Maxio (the billing system of record).
/// </summary>
public class SubscriptionDto
{
    public long SubscriptionId { get; set; }

    public string? Reference { get; set; }

    /// <summary>Maxio subscription state; see Subscription-State.yaml in the Maxio spec.</summary>
    public string State { get; set; } = string.Empty;

    public long? CustomerId { get; set; }

    public string? CustomerEmail { get; set; }

    public long ProductId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Price of the plan, in the minor unit (cents) of the subscription currency.</summary>
    public long? PlanPriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The next date/time a renewal is assessed (the next billing date).</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? ProductFamilyName { get; set; }

    public static SubscriptionDto FromMaxioSubscription(MaxioSubscription subscription)
    {
        MaxioProduct? product = subscription.Product;

        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State ?? string.Empty,
            CustomerId = subscription.Customer?.Id,
            CustomerEmail = subscription.Customer?.Email,
            ProductId = product?.Id ?? 0,
            PlanHandle = product?.Handle,
            PlanName = product?.Name,
            PlanPriceInCents = subscription.ProductPriceInCents ?? product?.PriceInCents,
            Interval = product?.Interval,
            IntervalUnit = product?.IntervalUnit,
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
            ProductFamilyHandle = product?.ProductFamily?.Handle,
            ProductFamilyName = product?.ProductFamily?.Name
        };
    }
}
