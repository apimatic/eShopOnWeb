using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionEndpoints : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                SubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var user = await ResolveUserAsync(httpContext, userManager);
                if (user is null)
                    return Results.Unauthorized();

                var plans = await subscriptionService.GetPlansAsync(cancellationToken);
                return Results.Ok(new ListSubscriptionPlansResponse { Plans = plans });
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");

        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SubscribeRequest request,
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                SubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var user = await ResolveUserAsync(httpContext, userManager);
                if (user is null)
                    return Results.Unauthorized();

                var result = await subscriptionService.SubscribeAsync(user, request.PlanHandle, cancellationToken);
                var response = new SubscribeResponse { Subscription = result.Subscription };
                return result.Created
                    ? Results.Created("api/subscriptions/" + result.Subscription.SubscriptionId, response)
                    : Results.Ok(response);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");

        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                HttpContext httpContext,
                UserManager<ApplicationUser> userManager,
                SubscriptionService subscriptionService,
                CancellationToken cancellationToken) =>
            {
                var user = await ResolveUserAsync(httpContext, userManager);
                if (user is null)
                    return Results.Unauthorized();

                var subscriptions = await subscriptionService.GetMySubscriptionsAsync(user, cancellationToken);
                return Results.Ok(new ListMySubscriptionsResponse { Subscriptions = subscriptions });
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    private static Task<ApplicationUser?> ResolveUserAsync(HttpContext httpContext, UserManager<ApplicationUser> userManager)
    {
        var userName = httpContext.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(userName)
            ? Task.FromResult<ApplicationUser?>(null)
            : userManager.FindByNameAsync(userName);
    }
}
