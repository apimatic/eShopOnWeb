using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Helpers shared by the subscription endpoints: resolving the authenticated shopper from the JWT
/// into a <see cref="SubscriberInfo"/> (keyed on the stable eShopOnWeb user id) and mapping domain
/// models to the API DTOs.
/// </summary>
internal static class SubscriptionMapping
{
    /// <summary>
    /// Resolves the eShopOnWeb user behind the bearer token into a <see cref="SubscriberInfo"/>.
    /// Returns <c>null</c> when the token carries no username or the user cannot be found.
    /// </summary>
    public static async Task<SubscriberInfo?> ResolveSubscriberAsync(
        ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? userName;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;

        return new SubscriberInfo
        {
            // The stable Identity user id is the idempotency key stored as the Maxio customer reference.
            Reference = user.Id,
            Email = email,
            FirstName = localPart,
            LastName = "eShopOnWeb"
        };
    }

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        IntervalCount = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };
}
