using System.Globalization;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio API models to the public subscription DTOs.
/// </summary>
public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToPlanDto(MaxioProduct product)
    {
        return new SubscriptionPlanDto
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? string.Empty,
            Description = product.Description,
            PriceInCents = product.PriceInCents,
            Price = FormatPrice(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty,
            ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty
        };
    }

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Price = FormatPrice(subscription.ProductPriceInCents),
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            CustomerId = subscription.Customer?.Id ?? 0
        };
    }

    public static string FormatPrice(long cents)
    {
        return "$" + (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
