using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Resolves the billing identity of the JWT-authenticated caller. The token carries the
/// username in the Name claim; the application user supplies the stable id and email that
/// key the Maxio customer reference.
/// </summary>
public static class BillingIdentity
{
    public static async Task<(MaxioSubscriber? Subscriber, IResult? Problem)> ResolveAsync(
        HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return (null, Results.Unauthorized());
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return (null, Results.Unauthorized());
        }

        var email = !string.IsNullOrWhiteSpace(user.Email) ? user.Email! : userName;
        return (new MaxioSubscriber(user.Id, userName, email), null);
    }
}
