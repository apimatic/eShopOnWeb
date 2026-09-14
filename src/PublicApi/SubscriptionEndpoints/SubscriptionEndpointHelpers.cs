using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointHelpers
{
    public static async Task<ApplicationUser?> ResolveAuthenticatedUserAsync(
        this ControllerBase endpoint,
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken = default)
    {
        string? userName = endpoint.User.Identity?.Name
                           ?? endpoint.User.FindFirstValue(ClaimTypes.Name)
                           ?? endpoint.User.FindFirstValue("unique_name");

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await userManager.FindByNameAsync(userName);
    }
}
