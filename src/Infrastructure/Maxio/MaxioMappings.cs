using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class MaxioMappings
{
    public const string DefaultProductHandle = "eshop-pro";

    public static string SubscriptionReference(string userId, string productHandle)
        => $"{userId}:{productHandle}";

    public static decimal CentsToCurrency(long cents) => cents / 100m;

    public static SubscriptionPlan ToPlan(ProductDto product)
    {
        return new SubscriptionPlan
        {
            Handle = product.Handle ?? string.Empty,
            Name = product.Name ?? product.Handle ?? string.Empty,
            Description = product.Description,
            Price = CentsToCurrency(product.PriceInCents),
            Interval = product.Interval,
            IntervalUnit = product.IntervalUnit ?? string.Empty
        };
    }

    public static CustomerSubscription ToCustomerSubscription(SubscriptionDto subscription)
    {
        var product = subscription.Product;
        var priceCents = subscription.ProductPriceInCents ?? product?.PriceInCents ?? 0;
        return new CustomerSubscription
        {
            Id = subscription.Id,
            ProductHandle = product?.Handle ?? string.Empty,
            ProductName = product?.Name ?? product?.Handle ?? string.Empty,
            Price = CentsToCurrency(priceCents),
            State = subscription.State ?? string.Empty,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            Reference = subscription.Reference
        };
    }

    public static bool IsLive(string? state)
    {
        return state is "active" or "trialing" or "assessing" or "pending"
            or "past_due" or "soft_failure" or "unpaid" or "paused" or "awaiting_signup";
    }

    public static string ResolveProductHandle(string? requestedHandle, IReadOnlyList<SubscriptionPlan> plans)
    {
        if (!string.IsNullOrWhiteSpace(requestedHandle))
        {
            return requestedHandle.Trim();
        }

        foreach (var plan in plans)
        {
            if (string.Equals(plan.Handle, DefaultProductHandle, System.StringComparison.OrdinalIgnoreCase))
            {
                return plan.Handle;
            }
        }

        return plans.Count > 0 ? plans[0].Handle : string.Empty;
    }
}
