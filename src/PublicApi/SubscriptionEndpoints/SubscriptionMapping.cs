using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps between domain subscription models and the API DTOs, and derives the billing
/// subscriber identity from the authenticated caller's JWT.
/// </summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };

    /// <summary>
    /// Builds the billing identity from the caller's token. Returns null when the token
    /// carries no usable identity (name). eShopOnWeb user names are email addresses.
    /// </summary>
    public static SubscriberIdentity? ToSubscriberIdentity(this ClaimsPrincipal user)
    {
        var userName = user.Identity?.Name
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var email = user.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            // eShopOnWeb identities use the email as the user name.
            email = userName;
        }

        return new SubscriberIdentity(userName, email);
    }
}
