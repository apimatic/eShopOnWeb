using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the caller (from the validated JWT) into a <see cref="SubscriberIdentity"/> for the billing layer.
/// The eShop user id is the stable idempotency key carried into Maxio as the customer reference.
/// </summary>
internal static class SubscriberResolver
{
    public static async Task<SubscriberIdentity> ResolveAsync(ClaimsPrincipal principal, UserManager<ApplicationUser> users)
    {
        var username = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("unique_name");

        if (string.IsNullOrEmpty(username))
        {
            throw new SubscriptionBillingException("The request is not authenticated.", 401);
        }

        var user = await users.FindByNameAsync(username);
        if (user is null)
        {
            throw new SubscriptionBillingException("The authenticated user no longer exists.", 401);
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? username : user.Email!;
        var (firstName, lastName) = DeriveName(email);

        return new SubscriberIdentity(user.Id, email, firstName, lastName);
    }

    // ApplicationUser (bare IdentityUser) carries no name fields; derive a reasonable display name from the
    // email local-part so Maxio's required first/last name are populated deterministically.
    private static (string FirstName, string LastName) DeriveName(string email)
    {
        var atIndex = email.IndexOf('@');
        var local = atIndex > 0 ? email.Substring(0, atIndex) : email;
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "eShopOnWeb");
    }
}
