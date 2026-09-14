using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The billing identity of the authenticated caller, derived from the JWT and the
/// Identity store. <see cref="Reference"/> is the stable eShopOnWeb user id used as
/// the Maxio customer reference (idempotency key).
/// </summary>
public sealed record SubscriberIdentity(string Reference, string Email, string FirstName, string LastName)
{
    /// <summary>
    /// Resolves the caller's billing identity from their token. The username claim
    /// identifies the Identity user; the immutable user id becomes the Maxio reference.
    /// </summary>
    public static async Task<SubscriberIdentity> ResolveAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriberIdentityException("The access token does not identify a user.");
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new SubscriberIdentityException($"No user found for '{userName}'.");
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        var localPart = email.Contains('@') ? email[..email.IndexOf('@')] : email;

        return new SubscriberIdentity(
            Reference: user.Id,
            Email: email,
            FirstName: string.IsNullOrWhiteSpace(localPart) ? "eShopOnWeb" : localPart,
            LastName: "eShopOnWeb");
    }
}

/// <summary>Raised when the authenticated caller cannot be mapped to a billing identity.</summary>
public sealed class SubscriberIdentityException : System.Exception
{
    public SubscriberIdentityException(string message) : base(message) { }
}
