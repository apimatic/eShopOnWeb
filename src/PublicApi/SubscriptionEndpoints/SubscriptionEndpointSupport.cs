using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    public static async Task<ShopperProfile> GetShopperAsync(
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var username = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new MaxioBillingException("The authenticated user could not be identified.", StatusCodes.Status401Unauthorized);
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new MaxioBillingException("An email address is required before subscribing.", StatusCodes.Status400BadRequest);
        }

        var nameParts = username.Split(new[] { '.', '_', '-', '@', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.FirstOrDefault() ?? "Shopper";
        var lastName = nameParts.Skip(1).FirstOrDefault() ?? user.Id;
        return new ShopperProfile(user.Id, user.Email, firstName, lastName);
    }

    public static SubscriptionResponse ToResponse(BillingSubscription subscription) =>
        new(subscription.Id, subscription.Reference, subscription.PlanHandle, subscription.PlanName, subscription.PriceInCents,
            subscription.Currency, subscription.State, subscription.NextBillingAt);
}
