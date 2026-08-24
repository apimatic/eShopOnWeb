using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated eShopOnWeb user from the JWT bearer token
/// (the token carries the username in its Name claim).
/// </summary>
public class CurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<(string UserId, string Email)> GetCurrentUserAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var username = principal?.FindFirst(ClaimTypes.Name)?.Value ?? principal?.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("The bearer token does not contain a username claim.");
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            throw new InvalidOperationException($"No user account exists for '{username}'.");
        }

        return (user.Id, user.Email ?? username);
    }
}
