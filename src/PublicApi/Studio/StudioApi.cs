using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Ties the studio feature to the eShop identity: who the caller is comes from the JWT, never from the request.
/// </summary>
public class StudioApi
{
    private readonly StudioService _studio;
    private readonly UserManager<ApplicationUser> _users;

    public StudioApi(StudioService studio, UserManager<ApplicationUser> users)
    {
        _studio = studio;
        _users = users;
    }

    public StudioService Studio => _studio;

    /// <summary>The signed-in shopper, read from the JWT's identity.</summary>
    public async Task<Shopper> GetCallerAsync(ClaimsPrincipal? principal)
    {
        var name = principal?.Identity?.Name ?? principal?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new StudioException(HttpStatusCode.Unauthorized, "The token does not say who you are.");
        }

        var user = await _users.FindByNameAsync(name).ConfigureAwait(false);
        if (user is null)
        {
            throw new StudioException(HttpStatusCode.Forbidden, "Your account no longer exists.");
        }

        return ToShopper(user);
    }

    /// <summary>A shopper named by a front-desk operator, by eShop user name or email.</summary>
    public async Task<Shopper> FindShopperAsync(string? userNameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(userNameOrEmail))
        {
            throw StudioException.BadRequest("shopper is required: the shopper's eShop user name or email.");
        }

        var key = userNameOrEmail.Trim();
        var user = await _users.FindByNameAsync(key).ConfigureAwait(false)
            ?? await _users.FindByEmailAsync(key).ConfigureAwait(false);
        if (user is null)
        {
            throw StudioException.NotFound($"There is no shopper '{key}'.");
        }

        return ToShopper(user);
    }

    private static Shopper ToShopper(ApplicationUser user) =>
        new(user.Id, user.UserName ?? user.Email ?? user.Id, user.Email ?? user.UserName ?? user.Id);
}
