using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionUserResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionUserResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriptionUser> ResolveAsync(HttpContext context)
    {
        var username = context.User.Identity?.Name;
        var user = string.IsNullOrWhiteSpace(username)
            ? null
            : await _userManager.FindByNameAsync(username);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new SubscriptionBillingException(
                "authenticated_user_not_found",
                "The authenticated user could not be resolved.",
                StatusCodes.Status401Unauthorized);
        }

        var localPart = user.Email.Split('@', 2)[0];
        var firstName = string.Join(
            ' ',
            localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

        return new SubscriptionUser(
            user.Id,
            user.Email,
            string.IsNullOrWhiteSpace(firstName) ? "eShop" : firstName,
            "Customer");
    }
}
