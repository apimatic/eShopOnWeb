using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Banking;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public static class Shopper
{
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;
    public const string Admins = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS;

    /// <summary>The caller's identity comes from the JWT (never from the request).</summary>
    public static string Id(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ApiRequestException(HttpStatusCode.Unauthorized, "The token carries no user name.");
        }

        return Normalize(name);
    }

    public static string Normalize(string userName) => userName.Trim().ToLowerInvariant();

    /// <summary>Finds the shopper support is asking about.</summary>
    public static async Task<string> FindAsync(UserManager<ApplicationUser> users, string email)
    {
        var user = await users.FindByEmailAsync(email) ?? await users.FindByNameAsync(email);
        if (user?.UserName is null)
        {
            throw new ApiRequestException(HttpStatusCode.NotFound, "No shopper has that email.");
        }

        return Normalize(user.UserName);
    }
}
