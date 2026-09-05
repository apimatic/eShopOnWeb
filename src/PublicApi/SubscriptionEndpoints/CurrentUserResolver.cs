using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the ApplicationUser behind the current JWT, and derives a display name for Maxio
/// customer records since ApplicationUser (an IdentityUser) does not carry first/last name.
/// </summary>
internal static class CurrentUserResolver
{
    public static async Task<ApplicationUser?> GetCurrentUserAsync(HttpContext httpContext)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(username);
    }

    public static (string FirstName, string LastName) DeriveDisplayName(ApplicationUser user)
    {
        var source = user.Email ?? user.UserName ?? "customer";
        var localPart = source.Contains('@') ? source[..source.IndexOf('@')] : source;
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, 2, StringSplitOptions.RemoveEmptyEntries);

        var firstName = parts.Length > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Length > 1 ? Capitalize(parts[1]) : "Customer";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
