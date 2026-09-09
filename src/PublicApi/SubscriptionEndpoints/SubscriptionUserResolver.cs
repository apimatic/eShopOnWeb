using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated principal into the identity details the billing
/// integration needs. The Identity user id becomes the Maxio customer reference.
/// </summary>
internal static class SubscriptionUserResolver
{
    public static async Task<SubscriptionUserInfo?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email : userName;
        var (firstName, lastName) = DeriveNames(email);

        return new SubscriptionUserInfo(user.Id, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        var firstName = Capitalize(tokens[0]);
        var lastName = tokens.Length > 1 ? Capitalize(tokens[^1]) : firstName;
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
