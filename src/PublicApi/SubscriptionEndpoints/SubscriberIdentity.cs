using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the calling JWT identity into a <see cref="CustomerRegistration"/>. The Maxio customer
/// reference is the eShopOnWeb user's immutable id, so the mapping survives an email change and
/// stays stable across requests.
/// </summary>
internal static class SubscriberIdentity
{
    public static async Task<CustomerRegistration?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? userName;
        var (firstName, lastName) = DeriveName(email);
        return new CustomerRegistration(user.Id, email, firstName, lastName);
    }

    private static (string First, string Last) DeriveName(string email)
    {
        var local = email.Contains('@') ? email[..email.IndexOf('@')] : email;
        var parts = local.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);

        static string Title(string value) =>
            string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

        if (parts.Length >= 2)
        {
            return (Title(parts[0]), Title(parts[^1]));
        }

        var first = parts.Length == 1 ? Title(parts[0]) : "eShop";
        return (first, "Subscriber");
    }
}
