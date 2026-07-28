using System;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between the subscription domain models and the API DTOs, and derives the billing
/// subscriber identity from the authenticated JWT principal.
/// </summary>
internal static class SubscriptionMapping
{
    /// <summary>
    /// Builds the billing <see cref="SubscriberIdentity"/> from the caller's token. The user name
    /// claim (an email in eShopOnWeb) is used as both the stable customer reference and email.
    /// Returns <c>null</c> when the principal carries no usable identity.
    /// </summary>
    public static SubscriberIdentity? ToSubscriberIdentity(this ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name
                       ?? principal?.FindFirstValue(ClaimTypes.Name)
                       ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = principal?.FindFirstValue(ClaimTypes.Email) ?? userName;
        return new SubscriberIdentity(Reference: userName, Email: email, FirstName: null, LastName: null);
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
    };
}
