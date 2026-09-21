using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Shared helpers for the subscription endpoints: resolving the caller's identity from the JWT and
/// mapping a <see cref="SubscriptionBillingException"/> onto a coherent HTTP response.
/// </summary>
internal static class SubscriptionEndpointSupport
{
    /// <summary>
    /// Resolves the authenticated caller (from the JWT name claim) to the durable eShop Identity user.
    /// Returns null when the token carries no usable identity or the user no longer exists.
    /// </summary>
    public static async Task<SubscriberInfo?> ResolveSubscriberAsync(ClaimsPrincipal user, UserManager<ApplicationUser> userManager)
    {
        var username = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return null;
        }

        var appUser = await userManager.FindByNameAsync(username);
        if (appUser is null)
        {
            return null;
        }

        return new SubscriberInfo(appUser.Id, appUser.UserName ?? username, appUser.Email);
    }

    /// <summary>Maps a billing failure to an HTTP result: caller's fault vs provider's, kept distinct.</summary>
    public static IResult ToProblem(SubscriptionBillingException ex) => ex.Kind switch
    {
        SubscriptionBillingErrorKind.InvalidRequest =>
            Results.BadRequest(new ProblemPayload(ex.Message)),
        SubscriptionBillingErrorKind.Validation =>
            Results.UnprocessableEntity(new ProblemPayload(ex.Message)),
        SubscriptionBillingErrorKind.ProviderUnavailable =>
            Results.Json(new ProblemPayload(ex.Message), statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new ProblemPayload("Unexpected error."), statusCode: StatusCodes.Status500InternalServerError)
    };

    public sealed record ProblemPayload(string Error);
}
