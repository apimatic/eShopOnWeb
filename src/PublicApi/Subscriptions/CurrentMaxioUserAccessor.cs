using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Builds the Maxio customer identity from the JWT-authenticated eShop user.</summary>
public sealed class CurrentMaxioUserAccessor
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentMaxioUserAccessor(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<MaxioCustomerInput?> GetAsync(ClaimsPrincipal principal)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;
        if (user is null || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        // eShopOnWeb's Identity user has no profile-name fields. Keep required Maxio
        // names useful without inventing a second customer profile in this integration.
        var localPart = email.Split('@', 2)[0];
        return new MaxioCustomerInput(user.Id, email, localPart, "Customer");
    }
}
