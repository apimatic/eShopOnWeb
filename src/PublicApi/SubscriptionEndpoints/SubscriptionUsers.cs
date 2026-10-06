using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Derives the deterministic Maxio references and subscriber identity from the
/// eShopOnWeb user resolved out of the JWT.
/// </summary>
internal static class SubscriptionUsers
{
    /// <summary>
    /// Deterministic per-user reference prefix. The full reference is unique per
    /// eShopOnWeb user and is the key that makes customer provisioning idempotent.
    /// </summary>
    public const string CustomerReferencePrefix = "eshopweb-user-";

    public static string CustomerReferenceFor(string userId) =>
        $"{CustomerReferencePrefix}{userId}";

    /// <summary>
    /// Resolves the caller from the JWT (the token carries the username claim) to the
    /// ApplicationUser record, or null when the caller is unknown.
    /// </summary>
    public static async Task<ApplicationUser?> ResolveCurrentUserAsync(ClaimsPrincipal? user, UserManager<ApplicationUser> userManager)
    {
        var username = user?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }

    public static MaxioSubscriber BuildSubscriber(ApplicationUser user)
    {
        var email = user.Email ?? user.UserName ?? string.Empty;
        var (firstName, lastName) = DeriveNames(email);
        return new MaxioSubscriber(
            CustomerReferenceFor(user.Id),
            email,
            firstName,
            lastName);
    }

    /// <summary>
    /// Maxio requires first/last names; eShopOnWeb does not store them, so they are
    /// derived deterministically from the email local part.
    /// </summary>
    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var separatorIndex = email.IndexOf('@');
        var localPart = separatorIndex >= 0 ? email[..separatorIndex] : email;
        var parts = localPart
            .Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        var firstName = parts.Count > 0 ? Capitalize(parts[0]) : "eShopOnWeb";
        var lastName = parts.Count > 1 ? Capitalize(parts[^1]) : "Shopper";
        return (firstName, lastName);
    }

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}