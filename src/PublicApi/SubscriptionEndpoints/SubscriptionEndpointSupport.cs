using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointSupport
{
    public static async Task<SubscriptionShopper?> GetShopperAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var user = await userManager.FindByNameAsync(userName);
        var email = user?.Email ?? user?.UserName;
        return user is null || string.IsNullOrWhiteSpace(email)
            ? null
            : new SubscriptionShopper(user.Id, email);
    }

    public static IResult Error(Exception exception) => exception switch
    {
        SubscriptionRequestException => Results.Problem(
            title: "Invalid subscription request", detail: exception.Message,
            statusCode: StatusCodes.Status400BadRequest),
        SubscriptionConflictException => Results.Problem(
            title: "Subscription creation is in progress", detail: exception.Message,
            statusCode: StatusCodes.Status409Conflict),
        MaxioApiException maxio => Results.Problem(
            title: "Subscription billing provider error", detail: maxio.Message,
            statusCode: ProviderStatus(maxio.StatusCode)),
        _ => throw exception
    };

    private static int ProviderStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.UnprocessableEntity => StatusCodes.Status422UnprocessableEntity,
        HttpStatusCode.GatewayTimeout => StatusCodes.Status504GatewayTimeout,
        HttpStatusCode.TooManyRequests => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status502BadGateway
    };
}
