using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated ApplicationUser from the JWT bearer principal.
/// </summary>
internal static class CurrentUserResolver
{
    public static async Task<ApplicationUser?> ResolveAsync(
        IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        var username = httpContextAccessor.HttpContext?.User?
            .FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return await userManager.FindByNameAsync(username);
    }

    /// <summary>
    /// The stable application reference used to identify the user in Maxio. The username
    /// is preferred over the Identity id so the mapping survives identity store reseeds.
    /// </summary>
    public static string GetUserBillingReference(ApplicationUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            return user.UserName;
        }
        return !string.IsNullOrWhiteSpace(user.Email) ? user.Email : user.Id;
    }

    /// <summary>
    /// Derives a first/last name pair for the Maxio customer record from the user's
    /// username (an email address in eShopOnWeb), e.g. "jane.doe@example.com" -> Jane / Doe.
    /// </summary>
    public static (string FirstName, string LastName) DeriveCustomerName(ApplicationUser user)
    {
        var localPart = (user.UserName ?? user.Email ?? "customer").Split('@')[0];
        var tokens = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .ToArray();

        var firstName = Capitalize(tokens.Length > 0 ? tokens[0] : "eShop");
        var lastName = Capitalize(tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : "Subscriber");
        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }
        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
