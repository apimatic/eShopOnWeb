using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CurrentShopperResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentShopperResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscribeToPlanRequest?> ResolveAsync(ClaimsPrincipal principal, string? productHandle)
    {
        var user = await FindUserAsync(principal);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? string.Empty;
        var localPart = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "Shopper";
        }

        return new SubscribeToPlanRequest
        {
            UserId = user.Id,
            Email = email,
            FirstName = localPart,
            LastName = "Subscriber",
            ProductHandle = productHandle?.Trim() ?? string.Empty
        };
    }

    public async Task<string?> ResolveUserIdAsync(ClaimsPrincipal principal)
    {
        var user = await FindUserAsync(principal);
        return user?.Id;
    }

    private async Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal)
    {
        var user = await _userManager.GetUserAsync(principal);
        if (user is not null)
        {
            return user;
        }

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _userManager.FindByNameAsync(userName);
    }
}
