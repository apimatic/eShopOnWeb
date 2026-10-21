using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Shared by the subscription endpoints: who the caller is, and how billing errors map to HTTP.</summary>
public static class SubscriptionEndpointSupport
{
    public const string Tag = "SubscriptionEndpoints";

    /// <summary>
    /// The caller's identity from the JWT: the token carries the user name; the stable user id (the
    /// billing customer's reference) comes from the identity store. Null when the user no longer exists.
    /// </summary>
    public static async Task<SubscriberIdentity?> ResolveSubscriberAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> users)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName)) return null;

        var user = await users.FindByNameAsync(userName);
        if (user is null) return null;

        var email = string.IsNullOrWhiteSpace(user.Email) ? userName : user.Email;
        return new SubscriberIdentity(user.Id, email);
    }

    public static IResult UnknownUser() => Error(StatusCodes.Status401Unauthorized, "The signed-in user was not found.");

    /// <summary>Maps a billing failure to a status and a message that never carries the provider's raw response.</summary>
    public static IResult FromBillingException(SubscriptionBillingException ex, HttpContext httpContext)
    {
        var status = ex.Error switch
        {
            SubscriptionBillingError.UnknownPlan => StatusCodes.Status400BadRequest,
            SubscriptionBillingError.SignupInProgress => StatusCodes.Status409Conflict,
            SubscriptionBillingError.Rejected => StatusCodes.Status422UnprocessableEntity,
            SubscriptionBillingError.Throttled => StatusCodes.Status503ServiceUnavailable,
            SubscriptionBillingError.WritesDisabled => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status503ServiceUnavailable,
        };

        SetRetryAfter(httpContext, ex.RetryAfter);
        return Error(status, ex.Message);
    }

    public static void SetRetryAfter(HttpContext httpContext, TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait && wait > TimeSpan.Zero)
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static IResult Error(int status, string message) =>
        Results.Json(new ErrorDetails { StatusCode = status, Message = message }, statusCode: status);
}
