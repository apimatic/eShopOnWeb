using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps between billing domain models, API DTOs, and the authenticated caller.</summary>
internal static class SubscriptionMapping
{
    /// <summary>
    /// Builds a <see cref="BillingSubscriber"/> from the JWT identity. Returns null when the token
    /// carries no usable name claim. The authenticated username (email) is the eShopOnWeb user's stable
    /// identity and is used as the Maxio customer reference.
    /// </summary>
    public static BillingSubscriber? ToSubscriber(this ClaimsPrincipal user)
    {
        var userName = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return new BillingSubscriber(userId: userName, email: userName);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPrice,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };
}
