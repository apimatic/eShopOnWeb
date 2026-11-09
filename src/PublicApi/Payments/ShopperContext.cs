using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Stax;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Resolves who is calling, from the JWT only (never from the request body).</summary>
public class ShopperContext
{
    private readonly UserManager<ApplicationUser> _users;

    public ShopperContext(UserManager<ApplicationUser> users) => _users = users;

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256) return false;
        return MailAddress.TryCreate(email.Trim(), out var a) && a.Address == email.Trim() && email.Contains('.', System.StringComparison.Ordinal);
    }

    /// <summary>The caller's email. Fails with 401 when the token's account no longer exists (e.g. it was closed).</summary>
    public async Task<string> RequireShopperAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiRequestException(401, "The token carries no identity.");
        var user = await _users.FindByNameAsync(name);
        if (user is null)
            throw new ApiRequestException(401, "This account does not exist or has been closed.");
        return Normalize(user.Email ?? user.UserName ?? name);
    }

    public Task<ApplicationUser?> FindUserAsync(string email) => _users.FindByEmailAsync(email);

    public Task<IdentityResult> DeleteUserAsync(ApplicationUser user) => _users.DeleteAsync(user);

    public static bool HasAnyRole(ClaimsPrincipal p, string role) => p.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role);
}
