using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    /// <summary>
    /// The shopper behind the bearer token, or null when the token names no known user.
    /// </summary>
    public static async Task<BillingBuyer?> ResolveBuyerAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new BillingBuyer(userName, string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email);
    }

    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static SubscriptionDto ToDto(SubscriptionDetails subscription) => new()
    {
        Id = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents / 100m,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt
    };
}
