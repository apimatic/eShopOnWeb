using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Resolves the ApplicationUser from the caller's JWT claims. The token carries the
/// username in the <see cref="ClaimTypes.Name"/> claim (the seeded users use their email
/// address as username).
/// </summary>
public static class CurrentUserExtensions
{
    public static async Task<ApplicationUser?> FindByPrincipalAsync(
        this UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var username = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }
}
