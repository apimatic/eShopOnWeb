using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="SubscriberIdentity"/>. The stable
/// ASP.NET Identity user id is used as the Maxio customer reference; names are derived (the demo identity
/// model carries no first/last name).
/// </summary>
internal static class SubscriberIdentityResolver
{
    public static async Task<SubscriberIdentity?> ResolveAsync(HttpContext http)
    {
        var username = http.User.FindFirstValue(ClaimTypes.Name) ?? http.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        // UserManager is scoped — resolve it from the request scope, not a captured field.
        var userManager = http.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(username);

        var userId = user?.Id ?? username;                 // stable customer reference
        var email = !string.IsNullOrWhiteSpace(user?.Email)
            ? user!.Email!
            : (LooksLikeEmail(username) ? username : $"{userId}@users.eshoponweb.invalid");

        var (firstName, lastName) = DeriveName(email);
        return new SubscriberIdentity(userId, email, firstName, lastName);
    }

    private static bool LooksLikeEmail(string value) => value.Contains('@');

    private static (string First, string Last) DeriveName(string email)
    {
        var local = email;
        var at = email.IndexOf('@');
        if (at > 0)
        {
            local = email.Substring(0, at);
        }

        return (string.IsNullOrWhiteSpace(local) ? "eShop" : local, "eShopOnWeb");
    }
}
