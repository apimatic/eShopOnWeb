using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The authenticated shopper, resolved from the JWT (which carries only the user name).</summary>
public record SubscriptionCaller(string UserId, string Email, string DisplayName)
{
    public static async Task<SubscriptionCaller?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var name = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name)) return null;
        var user = await userManager.FindByNameAsync(name);
        if (user == null) return null;
        var email = user.Email ?? user.UserName ?? name;
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        return new SubscriptionCaller(user.Id, email, local);
    }
}
