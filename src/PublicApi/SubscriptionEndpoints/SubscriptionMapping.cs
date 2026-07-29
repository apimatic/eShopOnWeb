using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: identity resolution, money formatting, and
/// mapping ApplicationCore subscription models to API DTOs.
/// </summary>
internal static class SubscriptionMapping
{
    /// <summary>
    /// Builds a <see cref="SubscriberInfo"/> from the JWT caller. The username (login) is the stable
    /// external reference; in eShopOnWeb it is also the email address.
    /// </summary>
    public static SubscriberInfo? ResolveSubscriber(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = userName.Contains('@') ? userName : $"{userName}@eshoponweb.local";
        return new SubscriberInfo(userName, email);
    }

    public static string FormatMoney(int cents, string? currency)
    {
        var amount = cents / 100m;
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency!.ToUpperInvariant();
        return code == "USD" ? $"${amount:0.00}" : $"{amount:0.00} {code}";
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        FormattedPrice = FormatMoney(plan.PriceInCents, plan.Currency),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static CustomerSubscriptionDto ToDto(SubscriberSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        FormattedPrice = FormatMoney(subscription.PriceInCents, subscription.Currency),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };
}
