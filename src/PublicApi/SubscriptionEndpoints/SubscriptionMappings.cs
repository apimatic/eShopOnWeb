using System.Globalization;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps subscription domain models to API DTOs and derives the subscriber from the JWT.</summary>
internal static class SubscriptionMappings
{
    /// <summary>
    /// Builds a <see cref="SubscriberIdentity"/> from the authenticated principal. The identity comes
    /// solely from the token (the user name claim), never from the request body. Returns null when the
    /// principal carries no user name.
    /// </summary>
    public static SubscriberIdentity? ToSubscriber(this ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName) ? null : SubscriberIdentity.FromUserName(userName);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        FormattedPrice = FormatPrice(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.CurrentPriceInCents,
        FormattedPrice = FormatPrice(subscription.CurrentPriceInCents),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CustomerId = subscription.CustomerId,
        CreatedAt = subscription.CreatedAt,
    };

    private static string FormatPrice(long priceInCents) =>
        "$" + (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
