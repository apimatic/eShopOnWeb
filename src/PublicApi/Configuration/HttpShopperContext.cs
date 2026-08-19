using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Configuration;

public interface IShopperContext
{
    Task<Shopper> GetCurrentShopperAsync();
}

public class HttpShopperContext : IShopperContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public HttpShopperContext(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<Shopper> GetCurrentShopperAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var userName = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new InvalidOperationException("The caller is not authenticated.");
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            throw new InvalidOperationException($"No eShopOnWeb user found for '{userName}'.");
        }

        var email = user.Email ?? user.UserName ?? userName;
        var name = user.UserName ?? userName;
        return new Shopper(user.Id, email, name);
    }
}
