using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record CallerIdentity(string UserId, string UserName, string? Email);

/// <summary>Resolves the caller from the JWT. The token only carries the user name, so the user is looked up from it.</summary>
public class SubscriptionCaller
{
    private readonly IHttpContextAccessor _accessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionCaller(IHttpContextAccessor accessor, UserManager<ApplicationUser> userManager)
    {
        _accessor = accessor;
        _userManager = userManager;
    }

    public async Task<CallerIdentity?> ResolveAsync()
    {
        var name = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var user = await _userManager.FindByNameAsync(name);
        return user is null ? null : new CallerIdentity(user.Id, user.UserName ?? name, user.Email);
    }
}
