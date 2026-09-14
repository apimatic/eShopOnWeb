using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the subscriber the billing integration works with. The caller
/// never supplies their own identity on these endpoints - it comes from the token and nowhere else.
/// </summary>
public static class SubscriberResolver
{
    public static BillingSubscriber? FromPrincipal(ClaimsPrincipal? principal)
    {
        var userName = principal?.Identity?.Name
            ?? principal?.FindFirstValue(ClaimTypes.Name)
            ?? principal?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        // eShopOnWeb logs users in by e-mail address, so the name claim doubles as the address; a token
        // that carries an explicit e-mail claim wins over that fallback.
        var email = principal?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            email = userName.Contains('@') ? userName : null;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new BillingSubscriber(
            userName,
            email!,
            principal?.FindFirstValue(ClaimTypes.GivenName),
            principal?.FindFirstValue(ClaimTypes.Surname));
    }
}
