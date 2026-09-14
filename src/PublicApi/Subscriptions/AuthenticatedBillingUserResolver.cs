using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class AuthenticatedBillingUserResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthenticatedBillingUserResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<BillingUser?> ResolveAsync(ClaimsPrincipal principal)
    {
        ApplicationUser? user = null;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            user = await _userManager.FindByIdAsync(userId);
        }

        if (user is null && !string.IsNullOrWhiteSpace(principal.Identity?.Name))
        {
            user = await _userManager.FindByNameAsync(principal.Identity.Name);
        }

        if (user is null || string.IsNullOrWhiteSpace(user.UserName))
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName : user.Email;
        return new BillingUser(user.Id, user.UserName, email!);
    }
}
