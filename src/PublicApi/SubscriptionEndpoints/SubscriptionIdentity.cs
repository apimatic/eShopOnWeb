using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Models.MaxioBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the shopper identity for subscription billing from the authenticated caller.
/// The JWT carries the username; the eShop user id becomes the Maxio customer reference.
/// </summary>
public static class SubscriptionIdentity
{
    public static async Task<ShopperIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
            return null;

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
            return null;

        var email = !string.IsNullOrWhiteSpace(user.Email)
            ? user.Email!
            : $"{(user.UserName ?? username).Split('@')[0]}@users.noreply.eshoponweb.local";

        var (firstName, lastName) = DeriveName(user.UserName ?? username);

        return new ShopperIdentity(user.Id, user.UserName ?? username, email, firstName, lastName);
    }

    private static (string FirstName, string LastName) DeriveName(string username)
    {
        var namePart = username.Contains('@', StringComparison.Ordinal)
            ? username[..username.IndexOf('@', StringComparison.Ordinal)]
            : username;

        var segments = namePart.Split(new[] { '.', '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = segments.Length > 0 ? Capitalize(segments[0]) : "eShop";
        var lastName = segments.Length > 1 ? Capitalize(segments[1]) : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        value.Length == 0
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..];
}