using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Resolves the calling shopper from the JWT's identity.</summary>
public class ShopperAccessor
{
    private readonly UserManager<ApplicationUser> _users;

    public ShopperAccessor(UserManager<ApplicationUser> users) => _users = users;

    public async Task<ApplicationUser> UserAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        var user = string.IsNullOrEmpty(name) ? null : await _users.FindByNameAsync(name);
        if (user == null) throw new ApiException(401, "The account does not exist or has been closed.");
        return user;
    }

    /// <summary>The shopper's email: the key for the Stax customer and for the shopper's orders.</summary>
    public async Task<string> EmailAsync(ClaimsPrincipal principal)
    {
        var user = await UserAsync(principal);
        return user.Email ?? user.UserName!;
    }
}
