using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class ShopperIdentityFactory
{
    public static async Task<ShopperIdentity?> FromUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
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

        var email = user.Email ?? user.UserName ?? userName;
        var localPart = email.Contains('@') ? email.Split('@')[0] : email;
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        return new ShopperIdentity
        {
            UserId = user.Id,
            Email = email,
            FirstName = localPart,
            LastName = "eShopOnWeb"
        };
    }

    public static ShopperSubscriptionDto ToDto(ShopperSubscription subscription) =>
        new()
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            Currency = subscription.Currency,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit
        };
}
