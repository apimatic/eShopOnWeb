using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the JWT-authenticated caller into subscription user data.
/// The caller's identity comes exclusively from the bearer token.
/// </summary>
public interface ISubscriptionUserResolver
{
    /// <summary>
    /// Returns the current user, or null when the authenticated principal cannot
    /// be matched to an existing eShopOnWeb identity user.
    /// </summary>
    Task<SubscriptionUserData?> ResolveAsync(CancellationToken cancellationToken = default);
}

public class SubscriptionUserResolver : ISubscriptionUserResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionUserResolver(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<SubscriptionUserData?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var username = principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            return null;
        }

        return new SubscriptionUserData
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? user.UserName ?? string.Empty
        };
    }
}