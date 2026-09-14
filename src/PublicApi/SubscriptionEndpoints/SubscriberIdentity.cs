using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller (from the JWT) into a <see cref="BillingCustomerIdentity"/>.
/// The billing <c>reference</c> is derived deterministically from the user's email so the same
/// user always maps to the same Maxio customer — the anchor for idempotent customer creation —
/// even across application restarts (the in-memory database does not persist the user's numeric id).
/// </summary>
internal static class SubscriberIdentity
{
    public static async Task<BillingCustomerIdentity?> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var appUser = await users.FindByNameAsync(userName);
        var email = (appUser?.Email ?? userName).Trim();
        var normalizedEmail = email.ToLowerInvariant();

        var reference = $"eshoponweb-{normalizedEmail}";
        var localPart = normalizedEmail.Contains('@') ? normalizedEmail[..normalizedEmail.IndexOf('@')] : normalizedEmail;
        var firstName = string.IsNullOrWhiteSpace(localPart) ? "eShop" : localPart;

        return new BillingCustomerIdentity(reference, email, firstName, "eShopOnWeb");
    }
}
