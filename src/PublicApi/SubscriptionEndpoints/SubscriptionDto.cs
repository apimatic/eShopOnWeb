using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A Maxio subscription as surfaced to the shopper: plan, price, state and the next billing date.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; set; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>ISO 4217 currency code of the subscription.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Recurring price actually attached to the subscription (minor units).</summary>
    public long PriceInCents { get; set; }

    /// <summary>The plan (product) the subscription is on.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    /// <summary>When the current billing period ends / the next renewal is assessed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    internal static SubscriptionDto FromMaxio(MaxioSubscriptionDto source)
    {
        var product = source.Product;
        return new SubscriptionDto
        {
            Id = source.Id,
            State = source.State ?? string.Empty,
            Currency = string.IsNullOrWhiteSpace(source.Currency) ? "USD" : source.Currency,
            PriceInCents = source.ProductPriceInCents ?? product?.PriceInCents ?? 0,
            NextBillingDate = source.NextAssessmentAt ?? source.CurrentPeriodEndsAt,
            CurrentPeriodEndsAt = source.CurrentPeriodEndsAt,
            NextAssessmentAt = source.NextAssessmentAt,
            CreatedAt = source.CreatedAt,
            ActivatedAt = source.ActivatedAt,
            CanceledAt = source.CanceledAt,
            Plan = product is null
                ? null
                : new SubscriptionPlanDto
                {
                    Id = product.Id,
                    Handle = product.Handle ?? string.Empty,
                    Name = product.Name ?? string.Empty,
                    Description = product.Description,
                    PriceInCents = product.PriceInCents ?? 0,
                    Currency = source.Currency ?? "USD",
                    Interval = product.Interval ?? 0,
                    IntervalUnit = product.IntervalUnit ?? string.Empty,
                    PricePointId = product.ProductPricePointId ?? product.DefaultProductPricePointId,
                    RequiresPaymentMethod = product.RequireCreditCard ?? false,
                },
        };
    }
}
