using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the caller's bearer token into the <see cref="Subscriber"/> the billing boundary works
/// with. The identity comes from the token alone - a subscription endpoint never accepts a user
/// name or customer reference from the request body.
/// </summary>
/// <remarks>
/// The token issued by this API carries the user name (and roles) but no email claim, so the email
/// the billing provider requires is read from the Identity store, falling back to the user name
/// when that is itself an email address - which it is for every eShopOnWeb account.
/// </remarks>
public class SubscriberResolver
{
    private readonly UserManager<ApplicationUser> _userManager;

    public SubscriberResolver(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Subscriber> GetSubscriberAsync(ClaimsPrincipal? principal, string? firstName = null, string? lastName = null)
    {
        var userName = principal?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new SubscriptionBillingException(
                "The caller could not be identified from the bearer token.",
                HttpStatusCode.Unauthorized);
        }

        var user = await _userManager.FindByNameAsync(userName);
        var email = user?.Email;

        if (string.IsNullOrWhiteSpace(email) && userName.Contains('@'))
        {
            email = userName;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SubscriptionBillingException(
                "The signed-in account has no email address, which the billing provider requires in order to create a customer.",
                HttpStatusCode.UnprocessableEntity);
        }

        return new Subscriber(userName, email!, firstName, lastName);
    }
}
