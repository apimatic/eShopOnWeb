using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="SubscriberIdentity"/> for the
/// billing layer. The Maxio customer <c>reference</c> is the login name (stable and deterministic
/// across restarts) rather than the ApplicationUser id (a GUID regenerated on each in-memory seed),
/// so idempotency survives an app restart.
/// </summary>
public static class CurrentSubscriber
{
    public static async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        var email = string.IsNullOrWhiteSpace(user?.Email) ? username : user!.Email!;
        var (firstName, lastName) = DeriveName(email);

        return new SubscriberIdentity(Reference: username, Email: email, FirstName: firstName, LastName: lastName);
    }

    // Maxio requires a non-empty first and last name. eShopOnWeb users carry no name, so derive a
    // reasonable, stable value from the email local part.
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var local = email;
        var at = local.IndexOf('@');
        if (at > 0)
        {
            local = local.Substring(0, at);
        }

        var parts = local.Split(new[] { '.', '_', '+', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return (Capitalize(parts[0]), Capitalize(parts[^1]));
        }

        var single = string.IsNullOrWhiteSpace(local) ? "eShop" : Capitalize(local);
        return (single, "eShopOnWeb");
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value.Substring(1) : string.Empty);
    }
}
