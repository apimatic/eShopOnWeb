using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CurrentUserService : ICurrentUserService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<CurrentUser> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("A valid bearer token is required.");
        }

        ApplicationUser? user = null;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await _userManager.FindByIdAsync(userId);
        }

        var userName = principal.Identity.Name;
        if (user == null && !string.IsNullOrWhiteSpace(userName))
        {
            user = await _userManager.FindByNameAsync(userName);
        }

        if (user == null)
        {
            throw new UnauthorizedAccessException("The bearer token does not identify an eShopOnWeb user.");
        }

        return new CurrentUser(
            user.Id,
            user.UserName ?? user.Email ?? user.Id,
            user.Email ?? user.UserName ?? throw new UnauthorizedAccessException("The user has no email address."));
    }
}
