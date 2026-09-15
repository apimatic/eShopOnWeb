using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentity
{
    public static async Task<ApplicationUser?> GetRequiredUserAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }

    public static (string FirstName, string LastName) SplitName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "shopper";
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        if (string.IsNullOrWhiteSpace(local))
        {
            local = "Shopper";
        }

        return (local, "eShopOnWeb");
    }
}

internal static class ShopperSubscriptionMapping
{
    public static ShopperSubscriptionDto ToDto(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        State = subscription.State,
        NextBillingAt = subscription.NextBillingAt
    };
}
