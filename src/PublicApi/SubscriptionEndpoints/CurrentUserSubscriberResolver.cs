using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the authenticated caller into a <see cref="SubscriberInfo"/>
/// used to map the user to their Maxio customer.
/// </summary>
public interface ISubscriberResolver
{
    Task<SubscriberInfo?> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the caller's identity from the JWT (the Name claim) into the
/// eShopOnWeb application user, and derives the Maxio subscriber info.
/// </summary>
public class CurrentUserSubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserSubscriberResolver(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SubscriberInfo?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null || string.IsNullOrEmpty(user.Email))
        {
            return null;
        }

        var (firstName, lastName) = DisplayNameFromEmail(user.Email);

        return new SubscriberInfo(user.Id, user.Email, firstName, lastName);
    }

    /// <summary>
    /// Maxio requires first/last name for a new customer; derive a sensible
    /// display name from the eShopOnWeb username (an email address).
    /// </summary>
    private static (string FirstName, string LastName) DisplayNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        var parts = localPart.Split(new[] { '.', '_', '-', '+' }, System.StringSplitOptions.RemoveEmptyEntries);

        var firstName = Capitalize(parts.Length > 0 ? parts[0] : "eShop");
        var lastName = Capitalize(parts.Length > 1 ? parts[1] : "Customer");

        return (firstName, lastName);
    }

    private static string Capitalize(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}