using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// The caller identity this project puts in the JWT (see IdentityTokenClaimService):
/// ClaimTypes.Name is the username, which everywhere doubles as the BuyerId.
/// </summary>
public static class AuthenticatedUser
{
    public static string RequireIdentity(ClaimsPrincipal user)
    {
        var identity = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new ValidationFailureException("The access token carries no user identity.");
        }
        return identity;
    }

    public static bool IsAdmin(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}
