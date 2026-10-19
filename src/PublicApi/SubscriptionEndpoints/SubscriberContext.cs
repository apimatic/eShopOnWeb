using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The authenticated caller of a subscription endpoint, plus the request's cancellation.</summary>
public sealed record SubscriberContext(Subscriber Subscriber, CancellationToken RequestAborted)
{
    /// <summary>
    /// Builds the caller from the JWT: the token carries only the user name, so the e-mail is read
    /// from the Identity store (falling back to the user name). Returns null when the token has no name.
    /// </summary>
    public static async Task<SubscriberContext?> ResolveAsync(
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        CancellationToken requestAborted)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var appUser = await userManager.FindByNameAsync(userName);
        var email = string.IsNullOrWhiteSpace(appUser?.Email) ? userName : appUser!.Email!;
        return new SubscriberContext(new Subscriber(userName, email), requestAborted);
    }
}
