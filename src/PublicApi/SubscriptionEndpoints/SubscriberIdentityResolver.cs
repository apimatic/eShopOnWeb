using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the authenticated principal into the subscriber the billing calls are made on behalf of.
/// The shopper is always taken from the bearer token — never from request input — so a caller cannot
/// subscribe, or read subscriptions, on behalf of somebody else.
/// </summary>
public interface ISubscriberIdentityResolver
{
    Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal, string? firstName = null, string? lastName = null);
}

public class SubscriberIdentityResolver : ISubscriberIdentityResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberIdentityResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal, string? firstName = null, string? lastName = null)
    {
        var userName = principal?.Identity?.Name;

        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(userName!);
        var email = user?.Email;

        if (string.IsNullOrWhiteSpace(email))
        {
            // The identity store is not authoritative for a bearer token that is still valid (with the
            // in-memory provider it is rebuilt on every restart), so fall back to the token's own name when
            // it already is an e-mail address.
            if (!userName!.Contains('@'))
            {
                return null;
            }

            email = userName;
        }

        return new SubscriberIdentity(userName!, email!, firstName, lastName);
    }
}
