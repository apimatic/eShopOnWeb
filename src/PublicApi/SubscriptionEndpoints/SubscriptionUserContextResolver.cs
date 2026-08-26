using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (JWT) to the data the Maxio integration needs.
/// The stable Identity user id becomes the Maxio customer reference.
/// </summary>
public static class SubscriptionUserContextResolver
{
    public static async Task<SubscriptionUserContext?> ResolveAsync(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var username = principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null || string.IsNullOrEmpty(user.Email))
        {
            return null;
        }

        var (firstName, lastName) = DeriveName(user.Email);
        return new SubscriptionUserContext(user.Id, user.Email, firstName, lastName);
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split('.', System.StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("eShop", "Customer"),
            1 => (Capitalize(parts[0]), "Customer"),
            _ => (Capitalize(parts[0]), Capitalize(parts[^1]))
        };
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
