using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class BillingBuyerResolver
{
    public static async Task<BillingBuyer?> ResolveAsync(UserManager<ApplicationUser> users, HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? userName;
        var localPart = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        var reference = user.UserName ?? user.Id;
        return new BillingBuyer(reference, email, localPart, "Customer");
    }
}

internal static class BillingDtoMapper
{
    public static SubscriptionPlanDto ToPlanDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description ?? string.Empty,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static CustomerSubscriptionDto ToSubscriptionDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle ?? string.Empty,
        ProductName = subscription.ProductName ?? string.Empty,
        Price = subscription.Price,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
