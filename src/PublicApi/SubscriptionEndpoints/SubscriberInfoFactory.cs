using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Builds the billing-system identity for the authenticated caller. The Maxio customer
/// reference is the eShopOnWeb identity user id, which is stable and unique per user.
/// </summary>
internal static class SubscriberInfoFactory
{
    public static async Task<SubscriberInfo?> CreateAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var username = principal.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? username : user.Email;
        var (firstName, lastName) = DeriveNames(email);

        return new SubscriberInfo
        {
            Reference = user.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }

    private static (string FirstName, string LastName) DeriveNames(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.FirstOrDefault() ?? "Subscriber";
        var lastName = parts.Skip(1).FirstOrDefault() ?? "eShopOnWeb";
        return (firstName, lastName);
    }
}
