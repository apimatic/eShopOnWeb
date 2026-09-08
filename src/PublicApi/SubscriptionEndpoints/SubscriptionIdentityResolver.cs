using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <inheritdoc cref="ISubscriptionIdentityResolver" />
public sealed class SubscriptionIdentityResolver : ISubscriptionIdentityResolver
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriptionIdentityResolver(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<SubscriptionIdentity?> ResolveAsync()
    {
        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = string.IsNullOrWhiteSpace(user.Email) ? user.UserName ?? userName : user.Email!;
        var (firstName, lastName) = DeriveDisplayName(email);

        return new SubscriptionIdentity(userName, email, firstName, lastName);
    }

    // The eShopOnWeb identity model stores no first/last name, yet Maxio requires them on the
    // customer record. Derive deterministic display names from the user's email address so the
    // customer is stable across repeated ensure-customer calls.
    private static (string FirstName, string LastName) DeriveDisplayName(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email.Substring(0, at) : email;
        var host = at > 0 ? email.Substring(at + 1) : string.Empty;

        var separator = local.IndexOf('.');
        var first = separator > 0 ? local.Substring(0, separator) : local;
        var last = separator > 0 ? local.Substring(separator + 1) : host;

        if (string.IsNullOrWhiteSpace(last))
        {
            last = "User";
        }

        return (first, last);
    }
}
