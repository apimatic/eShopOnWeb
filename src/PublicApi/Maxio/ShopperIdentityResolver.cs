using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Resolves the JWT-authenticated caller to a <see cref="ShopperIdentity"/> usable for
/// Maxio customer creation (which requires first/last name and email).
/// </summary>
public static class ShopperIdentityResolver
{
    public static async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        // The JWT carries only ClaimTypes.Name (username) — no NameIdentifier — so
        // UserManager.GetUserAsync (which reads NameIdentifier) cannot resolve the user.
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? string.Empty;
        var (firstName, lastName) = DeriveNames(email);
        return new ShopperIdentity(user.Id, email, firstName, lastName);
    }

    // eShopOnWeb identity stores no display name, so derive deliberate placeholders from
    // the email local part ("jane.doe@x" -> Jane / Doe; "demouser@x" -> Demouser / Shopper).
    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var local = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(local))
        {
            return ("eShop", "Shopper");
        }

        var parts = local.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var first = Capitalize(parts[0]);
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Shopper";
        return (first, last);
    }

    private static string Capitalize(string value) =>
        string.Concat(value[..1].ToUpperInvariant(), value.AsSpan(1));
}
