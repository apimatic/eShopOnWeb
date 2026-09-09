using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the eShopOnWeb user behind the JWT. Tokens carry the username in
/// the Name claim (there is no user-id claim), so the ApplicationUser is
/// looked up from the request's service provider.
/// </summary>
internal static class CurrentUser
{
    public static async Task<ApplicationUser?> ResolveAsync(ClaimsPrincipal principal, IServiceProvider requestServices)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }
        var userManager = requestServices.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByNameAsync(userName);
    }
}
