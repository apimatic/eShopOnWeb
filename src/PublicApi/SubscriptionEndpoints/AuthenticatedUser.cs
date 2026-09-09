using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated application user from the JWT bearer token.
/// </summary>
public static class AuthenticatedUser
{
    public static async Task<ApplicationUser?> GetAsync(
        Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var userName = httpContext?.User?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName)
            ?? throw new InvalidOperationException($"Authenticated user '{userName}' no longer exists.");
    }
}
