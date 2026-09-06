using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token of the caller into the subscriber the billing layer works with.
/// <para>
/// The token only carries the user name, so the identity store is the authority for the user id
/// and email. Resolving here rather than trusting a claim means a token can never be used to bill
/// an account other than the one it was issued for.
/// </para>
/// </summary>
public interface ISubscriberResolver
{
    /// <summary>
    /// Resolves the caller of the current request to a subscriber, or returns <c>null</c> when
    /// there is no authenticated caller or the principal does not match a known user.
    /// </summary>
    Task<SubscriberIdentity?> ResolveCurrentAsync(string? firstName = null, string? lastName = null);
}

/// <inheritdoc />
public sealed class SubscriberResolver : ISubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscriberResolver(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<SubscriberIdentity?> ResolveCurrentAsync(string? firstName = null, string? lastName = null)
    {
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        return new SubscriberIdentity
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? userName,
            FirstName = firstName,
            LastName = lastName
        };
    }
}
