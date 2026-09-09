using System;
using System.Globalization;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio Advanced Billing records onto the public API DTOs.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product, bool isDefault)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            PriceInCents = product.PriceInCents,
            Price = FormatPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
            IsDefault = isDefault
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        var product = subscription.Product ?? new MaxioProduct();
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            PlanHandle = product.Handle ?? string.Empty,
            PlanName = product.Name ?? string.Empty,
            PriceInCents = product.PriceInCents,
            Price = FormatPrice(product.PriceInCents),
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.NextBillingAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodEnd = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }

    private static string FormatPrice(int priceInCents)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${priceInCents / 100.0:0.00}");
    }
}
