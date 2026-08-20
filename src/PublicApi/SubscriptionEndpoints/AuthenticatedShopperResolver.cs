using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class AuthenticatedShopperResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthenticatedShopperResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriptionShopper?> ResolveAsync(
        ClaimsPrincipal principal,
        string? firstName = null,
        string? lastName = null)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        return new SubscriptionShopper(
            user.Id,
            user.Email ?? username,
            firstName,
            lastName);
    }
}
