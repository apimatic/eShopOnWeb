using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: resolving the caller's stable billing identity
/// from the JWT, and mapping <see cref="SubscriptionBillingException"/> to an HTTP problem result.
/// </summary>
public static class SubscriptionEndpointHelpers
{
    /// <summary>
    /// Resolves the authenticated caller (identified by the JWT <c>name</c> claim, which is the
    /// eShop username/email) to a <see cref="BillingAppUser"/> keyed on the stable
    /// <see cref="ApplicationUser.Id"/>. Returns <c>null</c> when the principal cannot be resolved.
    /// </summary>
    public static async Task<BillingAppUser?> ResolveBillingUserAsync(
        ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            return null;
        }

        var email = user.Email ?? user.UserName ?? userName;
        var (firstName, lastName) = DeriveName(email);
        return new BillingAppUser(user.Id, email, firstName, lastName);
    }

    /// <summary>
    /// Maps a billing failure to an HTTP status. Failures that are the provider's or our own
    /// (auth/quota/timeout/transport) become 5xx; failures the caller can act on (validation,
    /// not-found) are surfaced with their own 4xx status.
    /// </summary>
    public static IResult ToProblem(SubscriptionBillingException ex)
    {
        var status = ex.StatusCode switch
        {
            401 or 403 => StatusCodes.Status502BadGateway,        // our credentials — caller can't fix
            429 => StatusCodes.Status503ServiceUnavailable,       // our quota
            504 => StatusCodes.Status504GatewayTimeout,           // our deadline elapsed
            >= 400 and < 500 => ex.StatusCode.Value,              // caller-actionable (e.g. 404 plan, 422 validation)
            _ => StatusCodes.Status502BadGateway                  // 5xx / null / transport
        };

        return Results.Problem(detail: ex.Message, statusCode: status, title: "Subscription billing error");
    }

    private static (string FirstName, string LastName) DeriveName(string email)
    {
        // eShop identity stores no first/last name; derive a reasonable display name from the email
        // local-part for the Maxio customer record. No sensitive data is fabricated.
        var local = email.Split('@')[0];
        var firstName = string.IsNullOrWhiteSpace(local) ? "eShop" : local;
        return (firstName, "Shopper");
    }
}
